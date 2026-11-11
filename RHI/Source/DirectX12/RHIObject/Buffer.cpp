
/***********************************************************************************
* @file    Buffer.cpp
* @brief   RHI Buffer
* @details
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include <cmath>
#include <cfloat>
#include <algorithm>
#include "Buffer.h"

#include <DirectXMath.h>

// 必要なクラスをインクルード
// ========================================
#include "Device.h"
#include "CommandContext.h"
#include "Texture.h"
// ========================================

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../dx12_descriptorHeapManager.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_swapChain.h"
#include "../Object/dx12_device.h"
#include "../Object/dx12_graphicsCommandList.h"
#include "../dx12_uploadManager.h"

#include <cstring>
// ========================================

#include "../../ResourceBufferAllocator.h"

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ※以前は Renderer のヘッダー (objLoader / mesh / constant) を読んでいた。モデルの読み込みは Renderer に移した
// ========================================


/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

namespace RHI
{
	namespace ObjectInterface
	{
		namespace
		{
			// DirectXMath の行列を、シェーダー (mul(ベクトル, 行列) / column_major) 用に転置して保存する
			::DirectX::XMFLOAT4X4 transposeForShader(const ::DirectX::XMFLOAT4X4& m)
			{
				::DirectX::XMFLOAT4X4 result;
				::DirectX::XMStoreFloat4x4(&result, ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&m)));
				return result;
			}
		}

		Buffer::Buffer() {}
		Buffer::~Buffer()
		{
			Dispose();
		}

		RHI_RESULT Buffer::Initialize(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext)
		{
			// 例外処理 nullptr
			if (!iDevice || !iCommandContext)return RHI_FAILED_INVALID_ARGUMENT;

			// 静的ダウンキャスト
			// TODO : Phase 3 で抽象化を見直すまでは具象型へのキャストで扱う
			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			_descriptorHeapManager = std::make_unique<RHI::DirectX12::DescriptorHeapManager>();

			// DescriptorHeapの作成
			HRESULT hr = _descriptorHeapManager->Create(device->GetDevice()->GetDevice());

			if (FAILED(hr))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// ※レンダーターゲットビュー(RTV)は CommandContext が作る (リサイズ時に作り直すため)

			if (!makeCommonBuffer(device))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 頂点バッファのSRV (ヒープの 0 / 1 番)
			if (!makeCommonView(device))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 定数バッファは UPLOAD ヒープなので、生存中はずっと Map しておく (永続マップ)
			if (FAILED(_commonCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			if (FAILED(_globalCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			if (FAILED(_sceneCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 既定テクスチャ(白)を作る。ヒープの 2 番 = TextureID 0 になる
			// テクスチャを持たないマテリアルや読み込み失敗時はこれを使う (1-11)
			_texture = std::make_unique<RHI::ObjectInterface::Texture>();
			if (!_texture->CreateDefaultTexture(device, cmdContext, this))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// マテリアル : フレームごとの UPLOAD 領域と、同じ並びの VRAM 側 (GPU は VRAM 側を読む)
			{
				const UINT64 size = static_cast<UINT64>(MAX_MATERIALS) * sizeof(MaterialData) * CommandContext::FRAME_COUNT;
				_materialUpload = std::make_unique<RHI::DirectX12::ResourceBuffer>();
				_materialGpu    = std::make_unique<RHI::DirectX12::ResourceBuffer>();
				if (!makeBuffer(device, _materialUpload.get(), size, D3D12_HEAP_TYPE_UPLOAD) || FAILED(_materialUpload->Map()) ||
					!makeBuffer(device, _materialGpu.get(), size, D3D12_HEAP_TYPE_DEFAULT))
				{
					return RHI_FAILED_INITIALIZED;
				}
			}
			// 既定のマテリアル (0 番) : 白・既定テクスチャ・不透明。マテリアルを指定しない DrawItem はこれで描かれる
			CreateMaterial(RHI::MaterialDesc());

			// ライト : マテリアルと同じく、フレームごとの UPLOAD 領域と VRAM 側
			{
				const UINT64 size = static_cast<UINT64>(MAX_LIGHTS) * sizeof(RHI::LightData) * CommandContext::FRAME_COUNT;
				_lightUpload = std::make_unique<RHI::DirectX12::ResourceBuffer>();
				_lightGpu    = std::make_unique<RHI::DirectX12::ResourceBuffer>();
				if (!makeBuffer(device, _lightUpload.get(), size, D3D12_HEAP_TYPE_UPLOAD) || FAILED(_lightUpload->Map()) ||
					!makeBuffer(device, _lightGpu.get(), size, D3D12_HEAP_TYPE_DEFAULT))
				{
					return RHI_FAILED_INITIALIZED;
				}
			}

			return RHI_SUCCEEDED;
		}

		RHI::VertexRange Buffer::UploadVertexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const RHI::Vertex* vertices,
			uint32_t count
		)
		{
			RHI::VertexRange range;
			if (!iDevice || !iCommandContext || !vertices || count == 0) return range;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneVBAllocator->Allocate(count, sizeof(RHI::Vertex));
			if (!memoryAllocation.IsValid)
			{
				// 以前は容量オーバーでもオフセット0が返り、既存の頂点を上書きしていた (2-2)
				Core::Logger::Log("[Buffer] Scene vertex buffer is full.");
				return range;
			}

			const uint64_t dataSize = static_cast<uint64_t>(count) * sizeof(RHI::Vertex);
			if (!uploadToDefaultBuffer(device, cmdContext, _sceneVB.get(), memoryAllocation.ByteOffset, vertices, dataSize))
			{
				return range;
			}

			// シェーダーは g_MegaVB[BaseVertex + インデックスの値] で読む
			range.BaseVertex = memoryAllocation.StrideOffset;
			range.Count      = count;

			// GPU カリング用の境界球 (AABB の中心から一番遠い頂点までの距離)。頂点が CPU にある今のうちに計算する
			// (RHI::Vertex の位置は Core::Vector3 なので、XMVECTOR へは成分を並べて読む)
			auto loadPosition = [](const RHI::Vertex& v) { return ::DirectX::XMVectorSet(v.Position._x, v.Position._y, v.Position._z, 0.0f); };
			{
				::DirectX::XMVECTOR minV = loadPosition(vertices[0]);
				::DirectX::XMVECTOR maxV = minV;
				for (uint32_t i = 1; i < count; ++i)
				{
					const ::DirectX::XMVECTOR p = loadPosition(vertices[i]);
					minV = ::DirectX::XMVectorMin(minV, p);
					maxV = ::DirectX::XMVectorMax(maxV, p);
				}
				const ::DirectX::XMVECTOR center = ::DirectX::XMVectorScale(::DirectX::XMVectorAdd(minV, maxV), 0.5f);
				float radius2 = 0.0f;
				for (uint32_t i = 0; i < count; ++i)
				{
					const ::DirectX::XMVECTOR d = ::DirectX::XMVectorSubtract(loadPosition(vertices[i]), center);
					radius2 = (std::max)(radius2, ::DirectX::XMVectorGetX(::DirectX::XMVector3LengthSq(d)));
				}
				::DirectX::XMFLOAT4 bounds;
				::DirectX::XMStoreFloat4(&bounds, center);
				bounds.w = std::sqrt(radius2);
				_vertexBounds[range.BaseVertex] = bounds;
			}
			return range;
		}

		RHI::IndexRange Buffer::UploadIndexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const uint32_t* indices,
			uint32_t count
		)
		{
			RHI::IndexRange range;
			if (!iDevice || !iCommandContext || !indices || count == 0) return range;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneIBAllocator->Allocate(count, sizeof(uint32_t));
			if (!memoryAllocation.IsValid)
			{
				Core::Logger::Log("[Buffer] Scene index buffer is full.");
				return range;
			}

			const uint64_t dataSize = static_cast<uint64_t>(count) * sizeof(uint32_t);
			if (!uploadToDefaultBuffer(device, cmdContext, _sceneIB.get(), memoryAllocation.ByteOffset, indices, dataSize))
			{
				return range;
			}

			range.FirstIndex = memoryAllocation.StrideOffset;   // DrawIndexedInstanced の StartIndexLocation
			range.Count      = count;
			return range;
		}

		bool Buffer::uploadToDefaultBuffer(
			RHI::ObjectInterface::Device* device,
			RHI::ObjectInterface::CommandContext* cmdContext,
			RHI::DirectX12::ResourceBuffer* destBuffer,
			uint64_t destByteOffset,
			const void* data,
			uint64_t dataSize)
		{
			if (!destBuffer || !destBuffer->Get() || !data || dataSize == 0) return false;

			auto uploadManager = cmdContext->GetUploadManager();
			if (!uploadManager) return false;

			// 1. リングバッファから領域をもらって、CPU から直接書き込む
			//    (以前は毎回 UPLOAD バッファを新規作成 → Map → Unmap していた)
			RHI::DirectX12::UploadManager::Allocation allocation;
			if (!uploadManager->Allocate(dataSize, 16, allocation))
			{
				return false;
			}
			std::memcpy(allocation.Cpu, data, static_cast<size_t>(dataSize));

			// 2. COPY 用コマンドリストにコピー命令を積む
			auto cmdList = uploadManager->GetCommandList();
			if (!cmdList)
			{
				return false;
			}

			// バリアは張らない:
			//   COPY キューでは COMMON → COPY_DEST に暗黙で昇格し、完了後は COMMON に戻る(減衰)。
			//   描画キューで読む時も COMMON → 頂点(SRV) / インデックスの状態に暗黙で昇格する。
			//   (バッファはどの状態にも暗黙で昇格できる。Phase 1 まではここで明示的にバリアを張っていた)
			cmdList->CopyBufferRegion(
				destBuffer->Get(),             // コピー先 (VRAM)
				destByteOffset,                // コピー先オフセット
				allocation.Resource,           // コピー元 (リングバッファ)
				allocation.Offset,             // コピー元オフセット
				dataSize                       // コピーするサイズ
			);

			// 3. 送信はしない (RenderEnd でまとめて 1 回送信される)
			return true;
		}

		void Buffer::SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection)
		{
			// GPU が複数フレームを並行して処理するので、ここでは CPU 側の行列だけ更新し、
			// GPU へは WriteFrameConstants でフレームごとの領域に書く
			_viewMatrix = transposeForShader(view);
			_projMatrix = transposeForShader(projection);

			// ディファードのライティングで、深度から位置を戻すための逆行列と、カメラの位置 (反射の計算に使う)
			const ::DirectX::XMMATRIX v = ::DirectX::XMLoadFloat4x4(&view);
			const ::DirectX::XMMATRIX p = ::DirectX::XMLoadFloat4x4(&projection);
			const ::DirectX::XMMATRIX invViewProj = ::DirectX::XMMatrixInverse(nullptr, ::DirectX::XMMatrixMultiply(v, p));
			::DirectX::XMStoreFloat4x4(&_invViewProj, ::DirectX::XMMatrixTranspose(invViewProj));
			::DirectX::XMFLOAT4X4 invView;
			::DirectX::XMStoreFloat4x4(&invView, ::DirectX::XMMatrixInverse(nullptr, v));
			_cameraPosition[0] = invView._41;
			_cameraPosition[1] = invView._42;
			_cameraPosition[2] = invView._43;

			// 全フレームの領域に書き直しが必要になる
			_frameConstantsVersion++;
		}

		RHI::ObjectHandle Buffer::CreateObject()
		{
			RHI::ObjectHandle handle;

			// 初期値 : 原点に置く
			RHI::ObjectConstants initial;
			::DirectX::XMStoreFloat4x4(&initial.World, ::DirectX::XMMatrixIdentity());

			// 削除された番号があれば使い回す (オブジェクトの配列が増え続けないように)
			if (!_freeObjects.empty())
			{
				handle.Index = _freeObjects.back();
				_freeObjects.pop_back();
				_objects[handle.Index] = initial;
				_objectsVersion++;
				_objectVersion[handle.Index] = _objectsVersion;   // どのフレームの領域にも書き直す
				return handle;
			}

			if (_objects.size() >= MAX_OBJECTS)
			{
				Core::Logger::Log("[Buffer] Too many objects.");
				return handle;
			}

			handle.Index = static_cast<uint32_t>(_objects.size());

			_objects.push_back(initial);                 // 単位行列は転置しても同じ
			_objectVersion.push_back(_objectsVersion);
			for (auto& written : _objectWritten)
			{
				written.push_back(0);                    // どのフレームの領域にもまだ書いていない
			}
			_objectsVersion++;
			return handle;
		}

		void Buffer::SetScreenSize(uint32_t width, uint32_t height)
		{
			if (width == 0 || height == 0 || (width == _screenWidth && height == _screenHeight)) return;
			_screenWidth  = width;
			_screenHeight = height;
			_frameConstantsVersion++;
		}

		void Buffer::SetLights(const RHI::LightData* lights, uint32_t count)
		{
			count = (std::min)(count, MAX_LIGHTS);
			_lights.assign(lights, lights + (lights ? count : 0));
			_lightsVersion++;
			_frameConstantsVersion++;   // ライトの数がフレームの定数に入っている
		}

		void Buffer::SetLightingSettings(const RHI::LightingSettings& settings)
		{
			_lightingSettings = settings;
			_frameConstantsVersion++;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteLights(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList)
		{
			// マテリアルと同じ : 変わった時だけフレームの領域に書いて VRAM へ写す (ピクセルシェーダーが読むので VRAM に置く)
			const UINT64 frameOffset = static_cast<UINT64>(MAX_LIGHTS) * sizeof(RHI::LightData) * frameIndex;
			if (_lightsWritten[frameIndex] != _lightsVersion && cmdList)
			{
				if (!_lights.empty())
				{
					const UINT64 bytes = static_cast<UINT64>(_lights.size()) * sizeof(RHI::LightData);
					if (!_lightUpload->WriteData(frameOffset, _lights.data(), static_cast<size_t>(bytes))) return _lightGpu->GetGPUVirtualAddress() + frameOffset;
					cmdList->CopyBufferRegion(_lightGpu->Get(), frameOffset, _lightUpload->Get(), frameOffset, bytes);

					D3D12_RESOURCE_BARRIER barrier = {};
					barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
					barrier.Transition.pResource   = _lightGpu->Get();
					barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
					barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
					barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
					cmdList->ResourceBarrier(1, &barrier);
				}
				_lightsWritten[frameIndex] = _lightsVersion;
			}
			return _lightGpu->GetGPUVirtualAddress() + frameOffset;
		}

		void Buffer::UpdateMaterial(RHI::MaterialHandle material, const RHI::MaterialDesc& desc)
		{
			if (material.Index >= _materials.size()) return;
			MaterialData& data = _materials[material.Index];
			const uint32_t blend = static_cast<uint32_t>(desc.Blend);
			const uint8_t doubleSided = desc.DoubleSided ? 1 : 0;
			if (data.Blend != blend || _materialDoubleSided[material.Index] != doubleSided) _drawLayoutVersion++;   // PSO のまとまりが変わる
			for (int i = 0; i < 4; ++i) data.Color[i] = desc.Color[i];
			data.TextureIndex = desc.Texture.Index;
			data.Blend        = blend;
			data.Metallic     = desc.Metallic;
			data.Roughness    = desc.Roughness;
			_materialDoubleSided[material.Index] = doubleSided;
			_materialsVersion++;   // 全部のフレームの領域に書き直す
		}

		void Buffer::SetMaterialShader(RHI::MaterialHandle material, uint32_t shader, const float* params, uint32_t paramCount)
		{
			if (material.Index >= _materials.size()) return;
			if (_materialShader.size() < _materials.size())
			{
				_materialShader.resize(_materials.size(), 0);
				_materialParams.resize(_materials.size());
			}
			if (_materialShader[material.Index] != shader) _drawLayoutVersion++;   // まとめて描く / 自分のシェーダーで描く が変わる
			_materialShader[material.Index] = shader;
			paramCount = (std::min)(paramCount, RHI::MAX_CUSTOM_PARAMS);
			auto& values = _materialParams[material.Index];
			if (shader == 0 || !params || paramCount == 0) values.clear();
			else values.assign(params, params + static_cast<size_t>(paramCount) * 4);
		}

		const RHI::ObjectConstants* Buffer::GetObjectConstants(uint32_t object)const
		{
			return object < _objects.size() ? &_objects[object] : nullptr;
		}

		::DirectX::XMFLOAT3 Buffer::GetObjectPosition(uint32_t object)const
		{
			if (object >= _objects.size()) return { 0.0f, 0.0f, 0.0f };
			const auto& world = _objects[object].World;   // シェーダー用に転置済み (移動は 4 列目)
			return { world._14, world._24, world._34 };
		}

		void Buffer::DestroyMaterial(RHI::MaterialHandle material)
		{
			if (material.Index == RHI::MaterialHandle::DEFAULT || material.Index >= _materials.size()) return;
			if (std::find(_freeMaterials.begin(), _freeMaterials.end(), material.Index) != _freeMaterials.end()) return;   // 2 回返さない
			SetMaterialShader(material, 0, nullptr, 0);   // 自分のシェーダーも外す (SH2)
			UpdateMaterial(material, RHI::MaterialDesc());   // 白に戻しておく (間違って使われても目立たない)
			_freeMaterials.push_back(material.Index);
		}

		// ==============================================================
		// AN4 : GPU スキニング
		// ==============================================================

		RHI::SkinRange Buffer::UploadSkinData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext,
		                                      const RHI::SkinVertex* vertices, uint32_t count)
		{
			RHI::SkinRange range;
			if (!iDevice || !iCommandContext || !vertices || count == 0 || !_skinAllocator) return range;
			auto allocation = _skinAllocator->Allocate(count, sizeof(RHI::SkinVertex));
			if (!allocation.IsValid)
			{
				Core::Logger::Log("[Buffer] Skin buffer is full.");
				return range;
			}
			if (!uploadToDefaultBuffer(static_cast<RHI::ObjectInterface::Device*>(iDevice), static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext),
			                           _skinBuffer.get(), allocation.ByteOffset, vertices, static_cast<uint64_t>(count) * sizeof(RHI::SkinVertex)))
			{
				_skinAllocator->Free(allocation, sizeof(RHI::SkinVertex));
				return range;
			}
			range.Base  = allocation.StrideOffset;
			range.Count = count;
			return range;
		}

		void Buffer::FreeSkinData(const RHI::SkinRange& range)
		{
			if (!range.IsValid() || !_skinAllocator) return;
			RHI::ResourceBufferAllocation allocation;
			allocation.ByteOffset   = static_cast<uint64_t>(range.Base) * sizeof(RHI::SkinVertex);
			allocation.StrideOffset = range.Base;
			allocation.Count        = range.Count;
			allocation.IsValid      = true;
			_skinAllocator->Free(allocation, sizeof(RHI::SkinVertex));
		}

		RHI::SkinHandle Buffer::CreateSkinInstance(RHI::VertexRange source, RHI::SkinRange skin, uint32_t boneCount)
		{
			RHI::SkinHandle handle;
			if (!source.IsValid() || !skin.IsValid() || skin.Count != source.Count || boneCount == 0 || boneCount > MAX_SKIN_MATRICES) return handle;
			auto allocation = _sceneVBAllocator->Allocate(source.Count, sizeof(RHI::Vertex));
			if (!allocation.IsValid)
			{
				Core::Logger::Log("[Buffer] Scene vertex buffer is full (skinned instance).");
				return handle;
			}
			SkinInstance instance;
			instance.Source           = source;
			instance.Output.BaseVertex = allocation.StrideOffset;
			instance.Output.Count     = source.Count;
			instance.Skin             = skin;
			instance.BoneCount        = boneCount;
			instance.Alive            = true;
			instance.Dirty            = true;   // 最初の描画の前に、単位行列で休みの姿勢を書く
			instance.Matrices.assign(static_cast<size_t>(boneCount) * 12, 0.0f);
			for (uint32_t b = 0; b < boneCount; ++b)
			{
				float* m = instance.Matrices.data() + static_cast<size_t>(b) * 12;
				m[0] = 1.0f; m[5] = 1.0f; m[10] = 1.0f;   // 列 0 = (1,0,0,0) / 列 1 = (0,1,0,0) / 列 2 = (0,0,1,0)
			}
			// 境界球 : 休みの姿勢の球を少し大きく (曲げて少し出ても消えないように。出たら SetSkinBounds で広げる)
			::DirectX::XMFLOAT4 bounds = GetVertexBounds(source.BaseVertex);
			if (bounds.w >= 0.0f) bounds.w *= 1.5f;
			_vertexBounds[instance.Output.BaseVertex] = bounds;
			_drawLayoutVersion++;
			if (!_freeSkinInstances.empty())
			{
				handle.Index = _freeSkinInstances.back();
				_freeSkinInstances.pop_back();
				_skinInstances[handle.Index] = std::move(instance);
			}
			else
			{
				handle.Index = static_cast<uint32_t>(_skinInstances.size());
				_skinInstances.push_back(std::move(instance));
			}
			return handle;
		}

		RHI::VertexRange Buffer::GetSkinOutput(RHI::SkinHandle instance)const
		{
			return instance.Index < _skinInstances.size() && _skinInstances[instance.Index].Alive ? _skinInstances[instance.Index].Output : RHI::VertexRange();
		}

		void Buffer::SetSkinPose(RHI::SkinHandle instance, const float* matrices, uint32_t boneCount)
		{
			if (!matrices || instance.Index >= _skinInstances.size() || !_skinInstances[instance.Index].Alive) return;
			SkinInstance& skin = _skinInstances[instance.Index];
			const size_t count = static_cast<size_t>((std::min)(boneCount, skin.BoneCount)) * 12;
			if (std::memcmp(skin.Matrices.data(), matrices, count * sizeof(float)) == 0) return;   // 同じ姿勢なら曲げ直さない
			std::memcpy(skin.Matrices.data(), matrices, count * sizeof(float));
			skin.Dirty = true;
		}

		void Buffer::SetSkinBounds(RHI::SkinHandle instance, const float* sphere)
		{
			if (!sphere || instance.Index >= _skinInstances.size() || !_skinInstances[instance.Index].Alive) return;
			const uint32_t base = _skinInstances[instance.Index].Output.BaseVertex;
			auto found = _vertexBounds.find(base);
			if (found == _vertexBounds.end()) return;
			::DirectX::XMFLOAT4& current = found->second;
			const float dx = sphere[0] - current.x, dy = sphere[1] - current.y, dz = sphere[2] - current.z;
			const float distance = std::sqrt(dx * dx + dy * dy + dz * dz);
			if (current.w >= 0.0f && distance + sphere[3] <= current.w) return;   // 今の球に入っている
			// 両方を含む球を、少し余裕を持って (広げる回数を減らす。描画の引数を作り直すので)
			const float radius = (std::max)(sphere[3], distance + (std::max)(0.0f, current.w)) * 1.25f;
			current = ::DirectX::XMFLOAT4(sphere[0], sphere[1], sphere[2], radius);
			_drawLayoutVersion++;
		}

		void Buffer::DestroySkinInstance(RHI::SkinHandle instance)
		{
			if (instance.Index >= _skinInstances.size() || !_skinInstances[instance.Index].Alive) return;
			FreeVertexData(_skinInstances[instance.Index].Output);
			_skinInstances[instance.Index] = SkinInstance();
			_freeSkinInstances.push_back(instance.Index);
		}

		uint32_t Buffer::WriteSkinMatrices(uint32_t frameIndex, uint32_t written, const SkinInstance& instance)
		{
			if (!_skinMatrices || written + instance.BoneCount > MAX_SKIN_MATRICES) return UINT32_MAX;
			const uint64_t offset = (static_cast<uint64_t>(MAX_SKIN_MATRICES) * frameIndex + written) * 48ull;
			if (!_skinMatrices->WriteData(offset, instance.Matrices.data(), static_cast<size_t>(instance.BoneCount) * 48)) return UINT32_MAX;
			return written;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::GetSkinMatrixAddress(uint32_t frameIndex)const
		{
			return _skinMatrices ? _skinMatrices->GetGPUVirtualAddress() + static_cast<uint64_t>(MAX_SKIN_MATRICES) * 48ull * frameIndex : 0;
		}

		void Buffer::FreeVertexData(const RHI::VertexRange& range)
		{
			if (!range.IsValid() || !_sceneVBAllocator) return;
			RHI::ResourceBufferAllocation allocation;
			allocation.ByteOffset   = static_cast<uint64_t>(range.BaseVertex) * sizeof(RHI::Vertex);
			allocation.StrideOffset = range.BaseVertex;
			allocation.Count        = range.Count;
			allocation.IsValid      = true;
			_sceneVBAllocator->Free(allocation, sizeof(RHI::Vertex));
			_vertexBounds.erase(range.BaseVertex);
			_drawLayoutVersion++;   // GPU カリングの境界球を書き直させる
		}

		void Buffer::FreeIndexData(const RHI::IndexRange& range)
		{
			if (!range.IsValid() || !_sceneIBAllocator) return;
			RHI::ResourceBufferAllocation allocation;
			allocation.ByteOffset   = static_cast<uint64_t>(range.FirstIndex) * sizeof(uint32_t);
			allocation.StrideOffset = range.FirstIndex;
			allocation.Count        = range.Count;
			allocation.IsValid      = true;
			_sceneIBAllocator->Free(allocation, sizeof(uint32_t));
		}

		uint64_t Buffer::GetVertexBytesUsed()const { return _sceneVBAllocator ? _sceneVBAllocator->GetUsedBytes() : 0; }
		uint64_t Buffer::GetIndexBytesUsed()const  { return _sceneIBAllocator ? _sceneIBAllocator->GetUsedBytes() : 0; }

		RHI::MaterialHandle Buffer::CreateMaterial(const RHI::MaterialDesc& desc)
		{
			RHI::MaterialHandle handle;
			if (_freeMaterials.empty() && _materials.size() >= MAX_MATERIALS)
			{
				Core::Logger::Log("[Buffer] Too many materials. Use the default material.");
				return handle;
			}

			// 返された番号があれば使い回す (ED5)
			if (!_freeMaterials.empty())
			{
				handle.Index = _freeMaterials.back();
				_freeMaterials.pop_back();
				UpdateMaterial(handle, desc);
				return handle;
			}

			MaterialData data = {};
			for (int i = 0; i < 4; ++i) data.Color[i] = desc.Color[i];
			data.TextureIndex = desc.Texture.Index;
			data.Blend        = static_cast<uint32_t>(desc.Blend);
			data.Metallic     = desc.Metallic;
			data.Roughness    = desc.Roughness;

			handle.Index = static_cast<uint32_t>(_materials.size());
			_materials.push_back(data);
			_materialDoubleSided.push_back(desc.DoubleSided ? 1 : 0);
			_materialsVersion++;   // 全部のフレームの領域に書き直す
			return handle;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteMaterials(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList)
		{
			const UINT64 frameOffset = static_cast<UINT64>(MAX_MATERIALS) * sizeof(MaterialData) * frameIndex;
			if (_materialsWritten[frameIndex] != _materialsVersion && cmdList && !_materials.empty())
			{
				// マテリアルは数が少ないので、変わった時は全部書いて全部写す
				const UINT64 bytes = static_cast<UINT64>(_materials.size()) * sizeof(MaterialData);
				if (_materialUpload->WriteData(frameOffset, _materials.data(), static_cast<size_t>(bytes)))
				{
					cmdList->CopyBufferRegion(_materialGpu->Get(), frameOffset, _materialUpload->Get(), frameOffset, bytes);

					// コピーで COMMON → COPY_DEST に暗黙で昇格する。ピクセルシェーダーが読む状態へ移す
					// (以前は頂点シェーダーが読んでいた。補間する値を減らすため、今はピクセルシェーダーが引く)
					// (写さないフレームは COMMON → 読む状態に暗黙で昇格する)
					D3D12_RESOURCE_BARRIER barrier = {};
					barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
					barrier.Transition.pResource   = _materialGpu->Get();
					barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
					barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
					barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
					cmdList->ResourceBarrier(1, &barrier);
					_materialsWritten[frameIndex] = _materialsVersion;
				}
			}
			return _materialGpu->GetGPUVirtualAddress() + frameOffset;
		}

		void Buffer::DestroyObject(RHI::ObjectHandle object)
		{
			if (!object.IsValid() || object.Index >= _objects.size()) return;
			_freeObjects.push_back(object.Index);
		}

		void Buffer::UpdateObject(RHI::ObjectHandle object, const RHI::ObjectConstants& constants)
		{
			if (!object.IsValid() || object.Index >= _objects.size())
			{
				return;
			}

			RHI::ObjectConstants stored = constants;
			stored.World = transposeForShader(constants.World);
			_objects[object.Index] = stored;

			_objectsVersion++;
			_objectVersion[object.Index] = _objectsVersion;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteObjectConstants(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList)
		{
			if (!_sceneCB || frameIndex >= CommandContext::FRAME_COUNT)
			{
				return 0;
			}

			// このフレーム用の領域の先頭
			const uint64_t frameOffset = SCENE_CB_FRAME_SIZE * frameIndex;

			// 前回このフレームの領域に書いてから何も変わっていなければ、ループごと省く
			if (_objectsWrittenVersion[frameIndex] != _objectsVersion)
			{
				auto& written = _objectWritten[frameIndex];
				size_t first = SIZE_MAX, last = 0;   // 書いた範囲 (VRAM へコピーする範囲)
				for (size_t i = 0; i < _objects.size(); ++i)
				{
					// 変わったオブジェクトだけ書く (UPLOAD ヒープへの書き込みは遅いので最小限に)
					if (written[i] == _objectVersion[i]) continue;

					if (_sceneCB->WriteData(frameOffset + OBJECT_DATA_STRIDE * i, &_objects[i], sizeof(RHI::ObjectConstants)))
					{
						written[i] = _objectVersion[i];
						first = (std::min)(first, i);
						last  = (std::max)(last, i);
					}
				}
				_objectsWrittenVersion[frameIndex] = _objectsVersion;

				// 書いた範囲を VRAM 側へコピーする (間の変わっていない分も含めて 1 回で。止まっているシーンではコピー自体が無い)
				//   バッファは毎フレーム COMMON に戻るので、コピーでは COPY_DEST に暗黙で昇格する。
				//   コピー後は頂点シェーダー / GPU カリング (どちらも NON_PIXEL) が読む状態へ明示的に移す
				//   コピーしなかったフレームは COMMON → NON_PIXEL_SHADER_RESOURCE に暗黙で昇格する
				if (_objectGpu && cmdList && first <= last)
				{
					const UINT64 offset = frameOffset + OBJECT_DATA_STRIDE * first;
					cmdList->CopyBufferRegion(_objectGpu->Get(), offset, _sceneCB->Get(), offset, OBJECT_DATA_STRIDE * (last - first + 1));

					D3D12_RESOURCE_BARRIER barrier = {};
					barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
					barrier.Transition.pResource   = _objectGpu->Get();
					barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
					barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
					barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
					cmdList->ResourceBarrier(1, &barrier);
				}
			}

			// VRAM 側が作れなかった環境では、UPLOAD ヒープをそのまま読ませる (遅いが動く)
			return (_objectGpu ? _objectGpu->GetGPUVirtualAddress() : _sceneCB->GetGPUVirtualAddress()) + frameOffset;
		}

		::DirectX::XMFLOAT4 Buffer::GetVertexBounds(uint32_t baseVertex)const
		{
			const auto found = _vertexBounds.find(baseVertex);
			return found != _vertexBounds.end() ? found->second : ::DirectX::XMFLOAT4(0.0f, 0.0f, 0.0f, -1.0f);
		}

		const ::DirectX::XMFLOAT4* Buffer::GetFrustumPlanes()
		{
			if (_frustumVersion == _frameConstantsVersion) return _frustumPlanes;

			// 保持している行列はシェーダー用に転置済みなので戻してから掛ける (行ベクトル : clip = p * View * Proj)
			const ::DirectX::XMMATRIX view = ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&_viewMatrix));
			const ::DirectX::XMMATRIX proj = ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&_projMatrix));
			::DirectX::XMFLOAT4X4 m;
			::DirectX::XMStoreFloat4x4(&m, ::DirectX::XMMatrixMultiply(view, proj));

			// 列ベクトル c0〜c3 から平面を作る (Gribb / Hartmann の方法。深度は D3D の 0〜1)
			//   左 = c3 + c0 / 右 = c3 - c0 / 下 = c3 + c1 / 上 = c3 - c1 / 手前 = c2 / 奥 = c3 - c2
			auto column = [&](int c) { return ::DirectX::XMVectorSet(m.m[0][c], m.m[1][c], m.m[2][c], m.m[3][c]); };
			const ::DirectX::XMVECTOR c0 = column(0), c1 = column(1), c2 = column(2), c3 = column(3);
			const ::DirectX::XMVECTOR planes[6] = {
				::DirectX::XMVectorAdd(c3, c0), ::DirectX::XMVectorSubtract(c3, c0),
				::DirectX::XMVectorAdd(c3, c1), ::DirectX::XMVectorSubtract(c3, c1),
				c2,                             ::DirectX::XMVectorSubtract(c3, c2),
			};
			for (int i = 0; i < 6; ++i)
			{
				// xyz を正規化すると、平面との距離がそのままワールド座標の長さになる (半径と比べられる)
				::DirectX::XMStoreFloat4(&_frustumPlanes[i], ::DirectX::XMPlaneNormalize(planes[i]));
			}
			_frustumVersion = _frameConstantsVersion;
			return _frustumPlanes;
		}

		namespace
		{
			// 行ベクトル (p * M) の行列から視錐台の 6 平面 (GetFrustumPlanes と同じ方法)
			void extractPlanes(const ::DirectX::XMMATRIX& viewProj, ::DirectX::XMFLOAT4* out)
			{
				::DirectX::XMFLOAT4X4 m;
				::DirectX::XMStoreFloat4x4(&m, viewProj);
				auto column = [&](int c) { return ::DirectX::XMVectorSet(m.m[0][c], m.m[1][c], m.m[2][c], m.m[3][c]); };
				const ::DirectX::XMVECTOR c0 = column(0), c1 = column(1), c2 = column(2), c3 = column(3);
				const ::DirectX::XMVECTOR planes[6] = {
					::DirectX::XMVectorAdd(c3, c0), ::DirectX::XMVectorSubtract(c3, c0),
					::DirectX::XMVectorAdd(c3, c1), ::DirectX::XMVectorSubtract(c3, c1),
					c2,                             ::DirectX::XMVectorSubtract(c3, c2),
				};
				for (int i = 0; i < 6; ++i) ::DirectX::XMStoreFloat4(&out[i], ::DirectX::XMPlaneNormalize(planes[i]));
			}

			// 影を落とす物を、カスケードの範囲の外 (ライトの側) からも拾う距離 (ワールドの長さ)
			// 高い所にある物の影が、範囲の手前で切れないように
			constexpr float SHADOW_CASTER_DISTANCE = 60.0f;
		}

		void Buffer::SetShadowSettings(const RHI::ShadowSettings& settings, uint32_t shadowMapTexture, uint32_t resolution)
		{
			_shadowSettings   = settings;
			_shadowMapTexture = shadowMapTexture;
			_shadowResolution = (std::max)(1u, resolution);
			_frameConstantsVersion++;
		}

		void Buffer::computeShadowViews()
		{
			// ==============================================================
			// カスケードごとのライトの行列 (平行光源 → 正射影)
			// (下の手順 1〜4。手順 5 は上の説明)
			//   1. カメラの視錐台を奥行きで分ける (対数と均等の混ぜ合わせ : SplitLambda)
			//   2. 各範囲を囲む球を作る (半径はカメラの向きに関係なく同じ = 回しても影の大きさが変わらない)
			//   3. 球の中心をライトの視点のテクセル単位に丸める (カメラが動いても影の縁がちらつかない)
			//   4. ライトの側へ SHADOW_CASTER_DISTANCE 伸ばして、範囲の外の物の影も拾う
			// ==============================================================
			//   5. FrozenMask のカスケードは作り直さない (このフレームは描き直さないので、描いた時の行列のまま読む : 更新の間引き)
			// ==============================================================
			_shadowViewCount = 0;
			const RHI::ShadowSettings& s = _shadowSettings;
			if (!s.Enabled || s.LightIndex >= _lights.size() || _lights[s.LightIndex].Type != RHI::LightType::Directional)
			{
				for (bool& valid : _shadowValid) valid = false;
				return;
			}

			// カメラの行列 (保持しているのは転置済み) から、位置・向き・視野を取り出す
			const ::DirectX::XMMATRIX view = ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&_viewMatrix));
			const ::DirectX::XMMATRIX proj = ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&_projMatrix));
			::DirectX::XMFLOAT4X4 p;
			::DirectX::XMStoreFloat4x4(&p, proj);
			if (p._34 == 0.0f) return;   // 平行投影のカメラは未対応 (影なし)
			const float nearZ = -p._43 / p._33;
			const float tanX  = 1.0f / p._11;
			const float tanY  = 1.0f / p._22;
			const float k     = tanX * tanX + tanY * tanY;   // 奥行き d の断面の角までの距離² = d² × k
			const ::DirectX::XMMATRIX invView = ::DirectX::XMMatrixInverse(nullptr, view);
			const ::DirectX::XMVECTOR cameraPos     = invView.r[3];
			const ::DirectX::XMVECTOR cameraForward = ::DirectX::XMVector3Normalize(invView.r[2]);

			const uint32_t count = (std::min)((std::max)(s.CascadeCount, 1u), RHI::MAX_SHADOW_CASCADES);
			const float farZ  = (std::max)(s.Distance, nearZ + 0.01f);
			const float lambda = (std::min)((std::max)(s.SplitLambda, 0.0f), 1.0f);

			// ライトの向き (光が進む向き) と、ライトの視点の回転 (位置なし)
			const RHI::LightData& light = _lights[s.LightIndex];
			::DirectX::XMVECTOR dir = ::DirectX::XMVector3Normalize(::DirectX::XMVectorSet(light.Direction[0], light.Direction[1], light.Direction[2], 0.0f));
			const float upDot = std::fabs(::DirectX::XMVectorGetY(dir));
			const ::DirectX::XMVECTOR up = upDot > 0.99f ? ::DirectX::XMVectorSet(1, 0, 0, 0) : ::DirectX::XMVectorSet(0, 1, 0, 0);
			const ::DirectX::XMMATRIX lightRotation    = ::DirectX::XMMatrixLookToLH(::DirectX::XMVectorZero(), dir, up);
			const ::DirectX::XMMATRIX lightRotationInv = ::DirectX::XMMatrixTranspose(lightRotation);   // 回転だけなので転置 = 逆

			// 全カスケードを囲む箱 (ライトの回転の空間の AABB)。まとめてカリングする時に使う
			::DirectX::XMVECTOR unionMin = ::DirectX::XMVectorReplicate(FLT_MAX);
			::DirectX::XMVECTOR unionMax = ::DirectX::XMVectorReplicate(-FLT_MAX);

			float splitNear = nearZ;
			for (uint32_t c = 0; c < count; ++c)
			{
				// 1. 範囲の奥の端
				const float t = static_cast<float>(c + 1) / static_cast<float>(count);
				const float logSplit = nearZ * std::pow(farZ / nearZ, t);
				const float uniSplit = nearZ + (farZ - nearZ) * t;
				const float splitFar = lambda * logSplit + (1.0f - lambda) * uniSplit;

				// 2. 範囲 [a, b] を囲む球 : 中心は視線の上。手前と奥の断面の角を通る位置 (奥の面より奥にはしない)
				//    段の境目をなめらかにつなぐため、手前の段の奥の端を少し伸ばす (次の段と重ねる)
				const float a = splitNear;
				const float b = (c + 1 < count) ? splitFar + (splitFar - splitNear) * s.CascadeBlend : splitFar;
				_shadowSplits[c] = splitFar;
				splitNear = splitFar;
				if ((s.FrozenMask & (1u << c)) && _shadowValid[c])
				{
					// 間引き : このカスケードは前に描いた時の行列のまま (囲む箱には入れる)
					const float frozenRadius = 1.0f / _shadowProj[c]._11;   // 正射影の幅 / 2
					const ::DirectX::XMMATRIX frozenInv = ::DirectX::XMMatrixInverse(nullptr, ::DirectX::XMLoadFloat4x4(&_shadowView[c]));
					const ::DirectX::XMVECTOR frozenEye = ::DirectX::XMVector3TransformNormal(frozenInv.r[3], lightRotation);
					const float frozenDepth = 1.0f / _shadowProj[c]._33;     // 奥行きの範囲
					unionMin = ::DirectX::XMVectorMin(unionMin, ::DirectX::XMVectorSubtract(frozenEye, ::DirectX::XMVectorSet(frozenRadius, frozenRadius, 0.0f, 0.0f)));
					unionMax = ::DirectX::XMVectorMax(unionMax, ::DirectX::XMVectorAdd(frozenEye, ::DirectX::XMVectorSet(frozenRadius, frozenRadius, frozenDepth, 0.0f)));
					continue;
				}
				float centerZ = 0.5f * (a + b) * (1.0f + k);
				if (centerZ > b) centerZ = b;
				float radius = std::sqrt((b - centerZ) * (b - centerZ) + b * b * k);
				radius = std::ceil(radius * 16.0f) / 16.0f;   // 計算の誤差で半径が揺れないように丸める

				// 3. 中心をライトの視点でテクセル単位に丸める
				const float texelWorld = 2.0f * radius / static_cast<float>(_shadowResolution);
				::DirectX::XMVECTOR center = ::DirectX::XMVectorMultiplyAdd(cameraForward, ::DirectX::XMVectorReplicate(centerZ), cameraPos);
				::DirectX::XMVECTOR lightSpace = ::DirectX::XMVector3TransformNormal(center, lightRotation);
				lightSpace = ::DirectX::XMVectorMultiply(::DirectX::XMVectorFloor(::DirectX::XMVectorDivide(lightSpace, ::DirectX::XMVectorReplicate(texelWorld))),
					::DirectX::XMVectorReplicate(texelWorld));
				center = ::DirectX::XMVector3TransformNormal(lightSpace, lightRotationInv);

				// 4. ライトの視点 : 中心からライトの側へ下がった所から、光の向きに見る
				const float back = radius + SHADOW_CASTER_DISTANCE;
				const ::DirectX::XMVECTOR eye = ::DirectX::XMVectorSubtract(center, ::DirectX::XMVectorScale(dir, back));
				const ::DirectX::XMMATRIX lightView = ::DirectX::XMMatrixLookToLH(eye, dir, up);
				const ::DirectX::XMMATRIX lightProj = ::DirectX::XMMatrixOrthographicLH(2.0f * radius, 2.0f * radius, 0.0f, back + radius);
				::DirectX::XMStoreFloat4x4(&_shadowView[c], lightView);
				::DirectX::XMStoreFloat4x4(&_shadowProj[c], lightProj);
				extractPlanes(::DirectX::XMMatrixMultiply(lightView, lightProj), _shadowPlanes[c]);
				_shadowNormalOffset[c] = s.NormalBias * texelWorld;
				_shadowValid[c]        = true;

				// 囲む箱 : ライトの回転の空間で [中心 ± 半径] × [視点, 視点 + 奥行き]
				const ::DirectX::XMVECTOR eyeLs = ::DirectX::XMVector3TransformNormal(eye, lightRotation);
				unionMin = ::DirectX::XMVectorMin(unionMin, ::DirectX::XMVectorSubtract(eyeLs, ::DirectX::XMVectorSet(radius, radius, 0.0f, 0.0f)));
				unionMax = ::DirectX::XMVectorMax(unionMax, ::DirectX::XMVectorAdd(eyeLs, ::DirectX::XMVectorSet(radius, radius, back + radius, 0.0f)));
			}
			for (uint32_t c = count; c < RHI::MAX_SHADOW_CASCADES; ++c) _shadowValid[c] = false;
			_shadowViewCount = count;

			::DirectX::XMFLOAT3 mn, mx;
			::DirectX::XMStoreFloat3(&mn, unionMin);
			::DirectX::XMStoreFloat3(&mx, unionMax);
			const ::DirectX::XMMATRIX unionProj = ::DirectX::XMMatrixOrthographicOffCenterLH(mn.x, mx.x, mn.y, mx.y, mn.z, mx.z);
			extractPlanes(::DirectX::XMMatrixMultiply(lightRotation, unionProj), _shadowUnionPlanes);
		}

		const ::DirectX::XMFLOAT4* Buffer::GetFrustumPlanes(uint32_t view)
		{
			if (view == 0 || view > _shadowViewCount) return GetFrustumPlanes();
			return _shadowPlanes[view - 1];
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::GetViewConstants(uint32_t frameIndex, uint32_t view)const
		{
			if (!_commonCB || frameIndex >= CommandContext::FRAME_COUNT || view >= VIEW_COUNT) return 0;
			// v1.5 : フレーム → カメラ → 視点 の順に並べる
			return _commonCB->GetGPUVirtualAddress() + FRAME_SLOT_SIZE * ((static_cast<uint64_t>(frameIndex) * RHI::MAX_CAMERAS + _cameraSlot) * VIEW_COUNT + view);
		}

		void Buffer::SetCameraViews(const RHI::CameraView* views, uint32_t count)
		{
			_cameraCount = (std::min)(count, RHI::MAX_CAMERAS);
			for (uint32_t i = 0; i < _cameraCount; ++i) _cameraViews[i] = views[i];
			if (_cameraSlot >= _cameraCount) _cameraSlot = 0;
		}

		bool Buffer::SelectCamera(uint32_t camera)
		{
			if (camera >= _cameraCount) return false;
			_cameraSlot = camera;
			SetViewProjection(_cameraViews[camera].View, _cameraViews[camera].Projection);   // 影のカスケード / 視錐台も作り直す
			return true;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteFrameConstants(uint32_t frameIndex)
		{
			if (!_commonCB || frameIndex >= CommandContext::FRAME_COUNT)
			{
				return 0;
			}

			// フレームごとに 視点の数 × FRAME_SLOT_SIZE ずつ領域を分けている (Root CBV は 256byte 境界が必須)
			// 以前はフレームごとに 256byte (カメラだけ)
			const uint64_t offset = FRAME_SLOT_SIZE * VIEW_COUNT * (static_cast<uint64_t>(frameIndex) * RHI::MAX_CAMERAS + _cameraSlot);   // v1.5 : カメラごとの枠

			// 何も変わっていなければ書き込みを省く (UPLOAD ヒープは書き込みが遅いメモリなので、無駄に書かない)
			// 以前は View / Projection だけだった。ライティングの情報も足した (シェーダーの cbuffer FrameData と同じ並び)
			if (_writtenVersion[frameIndex][_cameraSlot] != _frameConstantsVersion)
			{
				FrameConstants constants = {};
				constants.View             = _viewMatrix;
				constants.Projection       = _projMatrix;
				constants.InvViewProj      = _invViewProj;
				for (int i = 0; i < 3; ++i)
				{
					constants.CameraPosition[i] = _cameraPosition[i];
					constants.Ambient[i]        = _lightingSettings.Ambient[i];
				}
				constants.LightCount       = static_cast<uint32_t>(_lights.size());
				constants.LightingModel    = static_cast<uint32_t>(_lightingSettings.Model);
				constants.ScreenSize[0]    = static_cast<float>(_screenWidth);
				constants.ScreenSize[1]    = static_cast<float>(_screenHeight);
				constants.InvScreenSize[0] = 1.0f / static_cast<float>(_screenWidth);
				constants.InvScreenSize[1] = 1.0f / static_cast<float>(_screenHeight);

				// 影 : カメラ / ライトが変わった時だけ作り直す (ここに来るのはその時だけ)
				computeShadowViews();
				constants.ShadowLightIndex   = _shadowViewCount > 0 ? _shadowSettings.LightIndex : 0xFFFFFFFFu;
				constants.ShadowMapTexture   = _shadowMapTexture;
				constants.ShadowCascadeCount = _shadowViewCount;
				constants.ShadowTexelSize    = 1.0f / static_cast<float>(_shadowResolution);
				constants.ShadowDistance     = _shadowViewCount > 0 ? _shadowSplits[_shadowViewCount - 1] : 0.0f;
				constants.ShadowFadeStart    = constants.ShadowDistance * 0.9f;
				constants.ShadowSoftness     = _shadowSettings.Softness;
				constants.ShadowBlend        = (std::min)((std::max)(_shadowSettings.CascadeBlend, 0.0f), 0.5f);
				constants.ShadowShowCascades = (_shadowSettings.Flags & RHI::SHADOW_FLAG_SHOW_CASCADES) ? 1u : 0u;
				for (uint32_t c = 0; c < _shadowViewCount; ++c)
				{
					const ::DirectX::XMMATRIX viewProj = ::DirectX::XMMatrixMultiply(
						::DirectX::XMLoadFloat4x4(&_shadowView[c]), ::DirectX::XMLoadFloat4x4(&_shadowProj[c]));
					::DirectX::XMStoreFloat4x4(&constants.ShadowViewProj[c], ::DirectX::XMMatrixTranspose(viewProj));
					constants.ShadowSplits[c]       = _shadowSplits[c];
					constants.ShadowNormalOffset[c] = _shadowNormalOffset[c];
				}

				bool written = _commonCB->WriteData(offset, &constants, sizeof(constants));

				// 影の視点 : カメラの定数を元に、View / Projection だけライトのものにする (影の項目は使わない)
				for (uint32_t c = 0; c < _shadowViewCount && written; ++c)
				{
					FrameConstants shadowView = constants;
					shadowView.View       = transposeForShader(_shadowView[c]);
					shadowView.Projection = transposeForShader(_shadowProj[c]);
					shadowView.ShadowCascadeCount = 0;
					written = _commonCB->WriteData(offset + FRAME_SLOT_SIZE * (c + 1), &shadowView, sizeof(shadowView));
				}

				if (written)
				{
					_writtenVersion[frameIndex][_cameraSlot] = _frameConstantsVersion;
				}
				else
				{
					Core::Logger::Log("[Buffer] Failed write frame constants.");
				}
			}

			return _commonCB->GetGPUVirtualAddress() + offset;
		}

		void Buffer::Dispose()
		{
			// GPUの完了待ちは CommandContext::Dispose で済ませてある前提
			// 以前は commonCB だけ Unmap していた
			if (_texture) { _texture->Dispose(); _texture.reset(); }

			if (_commonCB) _commonCB->Unmap();
			if (_globalCB) _globalCB->Unmap();
			if (_sceneCB)  _sceneCB->Unmap();
			_objectGpu.reset();
			if (_lightUpload) _lightUpload->Unmap();
			_lightUpload.reset();
			_lightGpu.reset();
			_lights.clear();
			if (_materialUpload) _materialUpload->Unmap();
			_materialUpload.reset();
			_materialGpu.reset();
			_materials.clear();
		}

		RHI::DirectX12::DescriptorHeapManager* Buffer::GetDescHeapManager()const
		{
			return _descriptorHeapManager.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalVB()const
		{
			return _globalVB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneVB()const
		{
			return _sceneVB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalCB()const
		{
			return _globalCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneCB()const
		{
			return _sceneCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetCommonCB()const
		{
			return _commonCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalIB()const
		{
			return _globalIB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneIB()const
		{
			return _sceneIB.get();
		}

		const uint64_t Buffer::GetSceneIndexBufferSize()const
		{
			return _SCENE_IB_SIZE;
		}

		bool Buffer::nullCheckManager()
		{
			return _descriptorHeapManager != nullptr;
		}

		bool Buffer::makeCommonBuffer(RHI::ObjectInterface::Device* device)
		{
			if (!device)return false;

			if(!makeAllocator()      )return false;
			if (!makeGlobalVB(device))return false;
			if ( !makeSceneVB(device))return false;
			if (!makeGlobalCB(device))return false;
			if ( !makeSceneCB(device))return false;
			if (!makeCommonCB(device))return false;
			if (!makeGlobalIB(device))return false;
			if (!makeSceneIB(device))return false;

			return true;
		}

		bool Buffer::makeGlobalVB(RHI::ObjectInterface::Device* device)
		{
			_globalVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalVBAllocator->Initialize(_GLOBAL_VB_SIZE);

			return makeBuffer(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeSceneVB(RHI::ObjectInterface::Device* device)
		{
			_sceneVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneVBAllocator->Initialize(_SCENE_VB_SIZE);

			// AN4 : GPU スキニングがコンピュートで書くので UAV にもできるようにする (読むのは今までどおり SRV)
			if (!makeBuffer(device, _sceneVB.get(), _SCENE_VB_SIZE, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS)) return false;

			// AN4 : 骨の番号と重み (VRAM) / 骨の行列 (UPLOAD、フレームごとの領域)
			_skinBuffer    = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			_skinAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_skinAllocator->Initialize(_SKIN_BUFFER_SIZE);
			_skinMatrices  = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			if (!makeBuffer(device, _skinBuffer.get(), _SKIN_BUFFER_SIZE, D3D12_HEAP_TYPE_DEFAULT)) return false;
			// UPLOAD は作った時に Map して開いたままにする (WriteData は Map した所に書く。以前は Map を忘れて書けなかった)
			return makeBuffer(device, _skinMatrices.get(), static_cast<uint64_t>(MAX_SKIN_MATRICES) * 48ull * CommandContext::FRAME_COUNT, D3D12_HEAP_TYPE_UPLOAD) &&
			       SUCCEEDED(_skinMatrices->Map());
		}

		bool Buffer::makeGlobalCB(RHI::ObjectInterface::Device* device)
		{
			_globalCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalCBAllocator->Initialize(_GLOBAL_CB_SIZE);

			return makeBuffer(
				device,
				_globalCB.get(),
				_GLOBAL_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		bool Buffer::makeSceneCB(RHI::ObjectInterface::Device* device)
		{
			_sceneCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneCBAllocator->Initialize(_SCENE_CB_SIZE);

			if (!makeBuffer(device, _sceneCB.get(), _SCENE_CB_SIZE, D3D12_HEAP_TYPE_UPLOAD))
			{
				return false;
			}

			// VRAM 側の写し (作れなくても UPLOAD ヒープを直接読ませて続けられる)
			_objectGpu = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			if (!makeBuffer(device, _objectGpu.get(), _SCENE_CB_SIZE, D3D12_HEAP_TYPE_DEFAULT))
			{
				_objectGpu.reset();
			}
			return true;
		}

		bool Buffer::makeCommonCB(RHI::ObjectInterface::Device* device)
		{
			_commonCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_commonCBAllocator->Initialize(_COMMON_CB_SIZE);

			return makeBuffer(
				device,
				_commonCB.get(),
				_COMMON_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		bool Buffer::makeGlobalIB(RHI::ObjectInterface::Device* device)
		{
			_globalIB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalIBAllocator->Initialize(_GLOBAL_IB_SIZE);

			return makeBuffer(
				device,
				_globalIB.get(),
				_GLOBAL_IB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeSceneIB(RHI::ObjectInterface::Device* device)
		{
			_sceneIB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneIBAllocator->Initialize(_SCENE_IB_SIZE);

			return makeBuffer(
				device,
				_sceneIB.get(),
				_SCENE_IB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeBuffer(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType,
		                        D3D12_RESOURCE_FLAGS flags)
		{
			const HRESULT hr = resourceBuffer->Create(
				device->GetDevice()->GetDevice(),
				sizeInBytes,
				heapType,
				flags
			);
			if (FAILED(hr))
			{
				// VRAM不足などで失敗する。どのサイズで失敗したかを残す
				char message[128] = {};
				sprintf_s(message, "[Buffer] Failed create buffer. size = %llu, HRESULT = 0x%08X", sizeInBytes, static_cast<unsigned int>(hr));
				Core::Logger::Log(message);
				return false;
			}
			return true;
		}

		bool Buffer::makeCommonView(RHI::ObjectInterface::Device* device)
		{
			if (!device)return false;

			// 作る順番 = ヒープの番号 (SRV_INDEX_GLOBAL_VB → SRV_INDEX_SCENE_VB)
			if (!makeGlobalVBV(device))return false;
			if ( !makeSceneVBV(device))return false;

#if 0
			// ※未使用 RootCBVを採用
			//if (!makeGlobalCBV(device))return false;
			//if ( !makeSceneCBV(device))return false;
#endif

			return true;
		}

		bool Buffer::makeGlobalVBV(RHI::ObjectInterface::Device* device)
		{
			if (!_globalVB.get()) return false;

			// 以前は return が無く、戻り値が不定だった (1-2)
			return makeVBView(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				sizeof(RHI::Vertex),
				SRV_INDEX_GLOBAL_VB
			);
		}

		bool Buffer::makeSceneVBV(RHI::ObjectInterface::Device* device)
		{
			if (!_sceneVB.get()) return false;

			// 以前は return が無く、戻り値が不定だった (1-2)
			return makeVBView(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				sizeof(RHI::Vertex),
				SRV_INDEX_SCENE_VB
			);
		}

		bool Buffer::makeAllocator()
		{
			_globalVBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneVBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();
			_globalCBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneCBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();
			_commonCBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_globalIBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneIBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();

			return true;
		}

		bool Buffer::makeVBView(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t structureByteStride, uint32_t expectedSrvIndex)
		{
			// 1. Managerから空いている「枠番号（インデックス）」をもらう
			const uint32_t srvIndex = _descriptorHeapManager->AllocateSrv();

			// 描画時はこの番号を決め打ちで参照するので、想定と違えば失敗にする
			if (srvIndex != expectedSrvIndex)
			{
				Core::Logger::Log("[Buffer] Unexpected SRV index for vertex buffer.");
				return false;
			}

			// 2. その枠のCPU側アドレスを取得
			D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle = _descriptorHeapManager->GetSrvCpuHandle(srvIndex);

			// 3. Bufferの情報を定義（メガバッファを StructuredBuffer のSRVとして登録する）
			D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
			srvDesc.Format                          = DXGI_FORMAT_UNKNOWN;
			srvDesc.ViewDimension                   = D3D12_SRV_DIMENSION_BUFFER;
			srvDesc.Shader4ComponentMapping         = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
			srvDesc.Buffer.FirstElement             = 0;
			srvDesc.Buffer.NumElements              = static_cast<UINT>(sizeInBytes / structureByteStride); // 頂点の総数
			srvDesc.Buffer.StructureByteStride      = structureByteStride;

			// 4. パレットの枠(cpuHandle)に、絵の具(Buffer)を登録！
			device->GetDevice()->GetDevice()->CreateShaderResourceView(resourceBuffer->Get(), &srvDesc, cpuHandle);

			return true;
		}
	}
}

/************************************
* End of file
************************************/
