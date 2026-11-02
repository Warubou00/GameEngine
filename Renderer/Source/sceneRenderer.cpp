
/***********************************************************************************
* @file    sceneRenderer.cpp
* @brief   シーンの描画 (モデル・描画オブジェクト・カメラ・描画リストの管理)
* @details
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "sceneRenderer.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <cwctype>
#include <filesystem>
#include <memory>

#include "mesh.h"
#include "material.h"
#include "Loader/objLoader.h"
#include "Loader/pmxLoader.h"

#include "../../Core/Source/Log/logger.h"

namespace Renderer
{
	namespace
	{
		// ObjLoader が new した Mesh と、その中の Material をまとめて解放する
		// (以前は Mesh も Material も解放されずリークしていた : 2-11)
		struct MeshDeleter
		{
			void operator()(Mesh* mesh)const
			{
				if (!mesh) return;
				for (auto* material : mesh->Materials)
				{
					delete material;
				}
				delete mesh;
			}
		};
		using MeshPtr = std::unique_ptr<Mesh, MeshDeleter>;

		::DirectX::XMFLOAT4X4 toFloat4x4(const ::DirectX::XMMATRIX& m)
		{
			::DirectX::XMFLOAT4X4 result;
			::DirectX::XMStoreFloat4x4(&result, m);
			return result;
		}
	}

	SceneRenderer::SceneRenderer() = default;
	SceneRenderer::~SceneRenderer()
	{
		Shutdown();
	}

	bool SceneRenderer::Initialize(const RHI::InitializeDesc& desc, RHI::InitializeResult* outResult)
	{
		if (_rhi)
		{
			Shutdown();
		}

		_rhi = RHI_CreateRenderHardwareInterface();
		if (!_rhi)
		{
			Core::Logger::Log("[SceneRenderer] Failed create RHI.");
			return false;
		}
		return _rhi->Initialize(desc, outResult);
	}

	void SceneRenderer::Shutdown()
	{
		if (!_rhi) return;

		// RHI の破棄は RHI.dll の中で行う (GPU の完了待ちも RHI のデストラクタで行われる)
		_rhi->Shutdown();
		RHI_DestroyRenderHardwareInterface(_rhi);
		_rhi = nullptr;
		_models.clear();
		_renderObjects.clear();
		_freeRenderObjects.clear();
		_aliveRenderObjects = 0;
		_drawItems.clear();
		_drawListDirty = false;
		_hasCamera     = false;
	}

	bool SceneRenderer::Resize(uint32_t width, uint32_t height)
	{
		if (!IsInitialized()) return false;
		if (!_rhi->Resize(width, height)) return false;

		// アスペクト比が変わるので Projection 行列を作り直す (以前は 1280x720 固定だった : 2-4)
		if (_hasCamera)
		{
			updateViewProjection();
		}
		return true;
	}

	// ==============================================================
	// モデル
	// ==============================================================

	uint32_t SceneRenderer::LoadModel(const std::wstring& filePath, uint32_t* outSubMeshCount)
	{
		if (outSubMeshCount) *outSubMeshCount = 0;
		if (!IsInitialized()) return INVALID_HANDLE;

		// 1. ファイルを読む (CPU 側)。拡張子でローダーを選ぶ
		std::wstring extension = std::filesystem::path(filePath).extension().wstring();
		std::transform(extension.begin(), extension.end(), extension.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
		std::unique_ptr<IModelLoader> loader;
		if      (extension == L".obj") loader = std::make_unique<ObjLoader>();
		else if (extension == L".pmx") loader = std::make_unique<PmxLoader>();
		else
		{
			Core::Logger::Log(L"[SceneRenderer] Unsupported model format (.obj / .pmx only).");
			Core::Logger::Log(filePath.c_str());
			return INVALID_HANDLE;
		}
		MeshPtr mesh(loader->LoadModel(filePath));
		if (!mesh)
		{
			Core::Logger::Log(L"[SceneRenderer] Failed load model.");
			Core::Logger::Log(filePath.c_str());
			return INVALID_HANDLE;
		}

		// 2. 頂点 (メッシュ全体で 1 回)
		const RHI::VertexRange vertices = _rhi->UploadVertices(mesh->Vertices.data(), static_cast<uint32_t>(mesh->Vertices.size()));
		if (!vertices.IsValid())
		{
			Core::Logger::Log("[SceneRenderer] Failed upload vertices.");
			return INVALID_HANDLE;
		}

		// 3. テクスチャ : 使うものだけ集めてまとめて読み込む (デコードは RHI の中で並列に行われる)
		//    同じパスは 1 回だけ読む (PMX は多くの材質が同じテクスチャを使う)
		std::vector<RHI::TextureHandle> materialTextures(mesh->Materials.size());
		{
			std::vector<std::wstring> paths;
			std::vector<size_t>       owners;     // マテリアル m のテクスチャは paths[owners[m]]
			owners.assign(mesh->Materials.size(), SIZE_MAX);
			for (size_t m = 0; m < mesh->Materials.size(); ++m)
			{
				const Material* material = mesh->Materials[m];
				if (!material || !material->UseTexture || material->TexturePath.empty()) continue;

				const std::wstring path = resolveTexturePath(material->TexturePath);
				auto found = std::find(paths.begin(), paths.end(), path);
				if (found == paths.end())
				{
					paths.push_back(path);
					found = paths.end() - 1;
				}
				owners[m] = static_cast<size_t>(found - paths.begin());
			}

			if (!paths.empty())
			{
				std::vector<const wchar_t*> pathPointers;
				for (const auto& path : paths) pathPointers.push_back(path.c_str());
				std::vector<RHI::TextureHandle> loaded(paths.size());
				_rhi->LoadTextures(pathPointers.data(), static_cast<uint32_t>(pathPointers.size()), loaded.data());
				for (size_t m = 0; m < owners.size(); ++m)
				{
					if (owners[m] != SIZE_MAX) materialTextures[m] = loaded[owners[m]];
				}
			}
		}

		// 4. サブメッシュごとに、インデックスとマテリアルを登録する (サブメッシュ i はマテリアル i を使う)
		//    マテリアル : テクスチャがあれば色は白 (テクスチャそのまま。不透明度だけ材質から)、無ければ材質の色
		//    半透明の材質 (PMX は全部) は、不透明な物の後にブレンドして描く
		//    描画オブジェクトはまだ作らない (以前は読み込みと同時に原点に 1 個置いていた)
		ModelResource model;
		for (size_t i = 0; i < mesh->Indecies.size(); ++i)
		{
			const auto& indices = mesh->Indecies[i];
			if (indices.empty()) continue;
			const RHI::IndexRange range = _rhi->UploadIndices(indices.data(), static_cast<uint32_t>(indices.size()));
			if (!range.IsValid())
			{
				Core::Logger::Log("[SceneRenderer] Failed upload indices.");
				continue;
			}

			RHI::MaterialDesc desc;
			const Material* material = i < mesh->Materials.size() ? mesh->Materials[i] : nullptr;
			if (material)
			{
				const bool textured = i < materialTextures.size() && materialTextures[i].Index != RHI::TextureHandle::DEFAULT;
				desc.Texture  = textured ? materialTextures[i] : RHI::TextureHandle();
				desc.Color[0] = textured ? 1.0f : material->Diffuse._x;
				desc.Color[1] = textured ? 1.0f : material->Diffuse._y;
				desc.Color[2] = textured ? 1.0f : material->Diffuse._z;
				desc.Color[3] = material->Transparent ? material->Diffuse._w : 1.0f;
				desc.Blend    = material->Transparent ? RHI::BlendMode::AlphaBlend : RHI::BlendMode::Opaque;
				desc.DoubleSided = material->DoubleSided;   // PMX の材質のフラグ (OBJ は常に片面)

				// PBR : OBJ / PMX は金属・粗さを持たないので、鏡面の鋭さ (OBJ の Ns / PMX の反射強度) から粗さを決める
				//       Blinn-Phong の指数 n と GGX の粗さの対応 : roughness = sqrt(2 / (n + 2))
				//       (キャラクターの肌などが光りすぎないよう 0.3 〜 1 に収める。指数が無ければ 0.6)
				const float power = material->Specular._w;
				desc.Metallic  = 0.0f;
				desc.Roughness = power > 0.0f ? (std::min)(1.0f, (std::max)(0.3f, std::sqrt(2.0f / (power + 2.0f)))) : 0.6f;
			}

			RHI::DrawItem item;
			item.BaseVertex = vertices.BaseVertex;
			item.FirstIndex = range.FirstIndex;
			item.IndexCount = range.Count;
			item.Material   = _rhi->CreateMaterial(desc);
			model.SubMeshes.push_back(item);
		}
		if (model.SubMeshes.empty())
		{
			return INVALID_HANDLE;
		}

		if (outSubMeshCount) *outSubMeshCount = static_cast<uint32_t>(model.SubMeshes.size());

		// 衝突判定のメッシュ用に、位置とインデックスだけ CPU 側に残す (頂点 12byte + インデックス 4byte。色や UV は捨てる)
		model.Positions.reserve(mesh->Vertices.size() * 3);
		for (const auto& v : mesh->Vertices)
		{
			model.Positions.push_back(v.Position._x);
			model.Positions.push_back(v.Position._y);
			model.Positions.push_back(v.Position._z);
		}
		for (const auto& indices : mesh->Indecies) model.Indices.insert(model.Indices.end(), indices.begin(), indices.end());

		_models.push_back(std::move(model));

				// 5. CPU 側のメッシュはここで解放される (GPU へのコピーは RHI のアップロード領域に済んでいる)
		return static_cast<uint32_t>(_models.size() - 1);
	}

	// ==============================================================
	// 描画オブジェクト
	// ==============================================================

	void SceneRenderer::CreateRenderObjects(uint32_t model, uint32_t count, uint32_t* outHandles)
	{
		if (!outHandles) return;
		for (uint32_t i = 0; i < count; ++i) outHandles[i] = INVALID_HANDLE;
		if (!IsInitialized() || model >= _models.size()) return;

		for (uint32_t n = 0; n < count; ++n)
		{
			// RHI のオブジェクト (ワールド行列) を 1 個作り、全サブメッシュで共有する (初期値は原点)
			RenderObject object;
			object.Model  = model;
			object.Object = _rhi->CreateObject();
			if (!object.Object.IsValid())
			{
				Core::Logger::Log("[SceneRenderer] Object limit reached.");
				_drawListDirty = true;   // ここまでに作った分は描く
				return;
			}

			// ハンドル : 削除された番号があれば使い回す
			uint32_t handle;
			if (!_freeRenderObjects.empty())
			{
				handle = _freeRenderObjects.back();
				_freeRenderObjects.pop_back();
				_renderObjects[handle] = std::move(object);
			}
			else
			{
				handle = static_cast<uint32_t>(_renderObjects.size());
				_renderObjects.push_back(std::move(object));
			}
			outHandles[n] = handle;
			_aliveRenderObjects++;
		}
		_drawListDirty = true;
	}

	void SceneRenderer::DestroyRenderObjects(const uint32_t* handles, uint32_t count)
	{
		if (!handles || !IsInitialized()) return;
		for (uint32_t n = 0; n < count; ++n)
		{
			const uint32_t handle = handles[n];
			if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE) continue;

			_rhi->DestroyObject(_renderObjects[handle].Object);
			_renderObjects[handle] = RenderObject();
			_freeRenderObjects.push_back(handle);
			_aliveRenderObjects--;
		}
		_drawListDirty = true;
	}

	void SceneRenderer::SetRenderObjectTransforms(const uint32_t* handles, const float* matrices, uint32_t count)
	{
		if (!handles || !matrices || !IsInitialized()) return;
		for (uint32_t n = 0; n < count; ++n)
		{
			const uint32_t handle = handles[n];
			if (handle >= _renderObjects.size()) continue;
			const RenderObject& object = _renderObjects[handle];
			if (object.Model == INVALID_HANDLE) continue;

			// C# の Matrix4x4 と XMFLOAT4X4 は同じ並び (行優先・行ベクトル) なのでそのまま写す
			// (オブジェクトは全サブメッシュで共有しているので 1 回で済む。以前はサブメッシュの数だけ書いていた)
			RHI::ObjectConstants constants;
			std::memcpy(&constants.World, matrices + static_cast<size_t>(n) * 16, sizeof(constants.World));
			_rhi->UpdateObject(object.Object, constants);
		}
	}

	std::wstring SceneRenderer::resolveTexturePath(const std::wstring& path)
	{
		namespace fs = std::filesystem;
		std::error_code error;
		if (fs::exists(path, error)) return path;

		// マテリアルに書かれた拡張子と、実際のファイルの拡張子が違うことがある (例 : .png と書いてあるが .jpg)
		static const wchar_t* const EXTENSIONS[] = { L".png", L".jpg", L".jpeg", L".tga", L".bmp", L".dds" };
		for (const wchar_t* extension : EXTENSIONS)
		{
			fs::path candidate(path);
			candidate.replace_extension(extension);
			if (fs::exists(candidate, error))
			{
				Core::Logger::Log((L"[SceneRenderer] Texture not found. Use " + candidate.wstring() + L" instead of " + path).c_str());
				return candidate.wstring();
			}
		}
		Core::Logger::Log((L"[SceneRenderer] Texture not found : " + path).c_str());
		return path;
	}

	void SceneRenderer::rebuildDrawList()
	{
		// 描画オブジェクトの番号の順に並べる (作った順に近い順 = インスタンシングと GPU のまとまりが良い)
		_drawItems.clear();
		_drawItems.reserve(_aliveRenderObjects * 2);
		for (const auto& object : _renderObjects)
		{
			if (object.Model == INVALID_HANDLE) continue;
			for (RHI::DrawItem item : _models[object.Model].SubMeshes)
			{
				item.Object = object.Object;
				_drawItems.push_back(item);
			}
		}
		_drawListDirty = false;
		_rhi->ReportProfilerValue("Render objects", static_cast<double>(_aliveRenderObjects));
		_rhi->ReportProfilerValue("Draw items", static_cast<double>(_drawItems.size()));
	}

	// ==============================================================
	// 設定 (RHI に渡すだけ)
	// ==============================================================

	void SceneRenderer::SetIndirectDrawEnabled(bool enabled)
	{
		if (_rhi) _rhi->SetIndirectDrawEnabled(enabled);
	}

	void SceneRenderer::SetInstancingEnabled(bool enabled)
	{
		if (_rhi) _rhi->SetInstancingEnabled(enabled);
	}

	void SceneRenderer::SetGpuCullingEnabled(bool enabled)
	{
		if (_rhi) _rhi->SetGpuCullingEnabled(enabled);
	}

	void SceneRenderer::SetOcclusionCullingEnabled(bool enabled)
	{
		if (_rhi) _rhi->SetOcclusionCullingEnabled(enabled);
	}

	// ==============================================================
	// カメラ / 描画
	// ==============================================================

	void SceneRenderer::SetCamera(const CameraDesc& camera)
	{
		_camera    = camera;
		_hasCamera = true;
		updateViewProjection();
	}

	bool SceneRenderer::Render(const RHI::RenderCommand* commands, uint32_t commandCount)
	{
		if (!IsInitialized()) return false;

		// 描画リストは描画オブジェクトが増減した時だけ作り直す (位置が変わっただけなら同じリストのまま)
		// RHI は描画リストが前回と同じなら、GPU 用の描画データも書き直さない
		if (_drawListDirty) rebuildDrawList();
		return _rhi->Render(_drawItems.data(), static_cast<uint32_t>(_drawItems.size()), commands, commandCount);
	}

	bool SceneRenderer::GetModelGeometry(uint32_t model, float* positions, uint32_t maxVertices, uint32_t* indices, uint32_t maxIndices,
	                                     uint32_t* outVertexCount, uint32_t* outIndexCount) const
	{
		if (model >= _models.size()) return false;
		const ModelResource& m = _models[model];
		const uint32_t vertexCount = static_cast<uint32_t>(m.Positions.size() / 3);
		const uint32_t indexCount  = static_cast<uint32_t>(m.Indices.size());
		if (outVertexCount) *outVertexCount = vertexCount;
		if (outIndexCount)  *outIndexCount  = indexCount;
		if (positions) std::memcpy(positions, m.Positions.data(), sizeof(float) * 3 * (std::min)(vertexCount, maxVertices));
		if (indices)   std::memcpy(indices, m.Indices.data(), sizeof(uint32_t) * (std::min)(indexCount, maxIndices));
		return true;
	}

	RHI::RenderTargetHandle SceneRenderer::CreateRenderTarget(const RHI::RenderTargetDesc& desc)
	{
		return IsInitialized() ? _rhi->CreateRenderTarget(desc) : RHI::RenderTargetHandle();
	}

	void SceneRenderer::DestroyRenderTarget(RHI::RenderTargetHandle handle)
	{
		if (IsInitialized()) _rhi->DestroyRenderTarget(handle);
	}

	void SceneRenderer::SetDebugLines(const RHI::DebugLineVertex* vertices, uint32_t count)
	{
		if (IsInitialized()) _rhi->SetDebugLines(vertices, count);
	}

	void SceneRenderer::SetShadowSettings(const RHI::ShadowSettings& settings)
	{
		if (IsInitialized()) _rhi->SetShadowSettings(settings);
	}

	uint32_t SceneRenderer::GetMaxSampleCount() const
	{
		return IsInitialized() ? _rhi->GetMaxSampleCount() : 1;
	}

	RHI::FullscreenShaderHandle SceneRenderer::LoadFullscreenShader(const wchar_t* filePath, const char* entry)
	{
		return IsInitialized() ? _rhi->LoadFullscreenShader(filePath, entry) : RHI::FullscreenShaderHandle();
	}

	void SceneRenderer::SetLights(const RHI::LightData* lights, uint32_t count)
	{
		if (IsInitialized()) _rhi->SetLights(lights, count);
	}

	void SceneRenderer::SetLightingSettings(const RHI::LightingSettings& settings)
	{
		if (IsInitialized()) _rhi->SetLightingSettings(settings);
	}

	void SceneRenderer::SetProfilerInfo(const char* name, const char* text)
	{
		if (_rhi) _rhi->SetProfilerInfo(name, text);
	}

	void SceneRenderer::WaitForGpu()
	{
		if (_rhi) _rhi->WaitForGpu();
	}

	void SceneRenderer::ReportProfilerValue(const char* name, double value)
	{
		if (_rhi) _rhi->ReportProfilerValue(name, value);
	}

	void SceneRenderer::updateViewProjection()
	{
		if (!IsInitialized()) return;

		const float width  = static_cast<float>(_rhi->GetWidth());
		const float height = static_cast<float>(_rhi->GetHeight());
		if (width <= 0.0f || height <= 0.0f) return;

		// 左手系 (+Z が奥)。どちらも転置しない普通の行列 (転置は RHI の中で行う)
		// 以前は Renderer::View / Projection クラスで、注視点 (Target) と固定の視野角 1.0 から作っていた
		const ::DirectX::XMVECTOR position = ::DirectX::XMVectorSet(_camera.Position[0], _camera.Position[1], _camera.Position[2], 1.0f);
		const ::DirectX::XMVECTOR forward  = ::DirectX::XMVectorSet(_camera.Forward[0],  _camera.Forward[1],  _camera.Forward[2],  0.0f);
		const ::DirectX::XMVECTOR up       = ::DirectX::XMVectorSet(_camera.Up[0],       _camera.Up[1],       _camera.Up[2],       0.0f);
		const ::DirectX::XMMATRIX view     = ::DirectX::XMMatrixLookToLH(position, forward, up);

		const float nearZ = (std::max)(_camera.NearZ, 0.0001f);
		const float farZ  = (std::max)(_camera.FarZ, nearZ + 0.001f);
		const ::DirectX::XMMATRIX projection = _camera.Orthographic != 0
			? ::DirectX::XMMatrixOrthographicLH(width, height, nearZ, farZ)
			: ::DirectX::XMMatrixPerspectiveFovLH(_camera.FieldOfViewY, width / height, nearZ, farZ);

		_rhi->SetViewProjection(toFloat4x4(view), toFloat4x4(projection));
	}
}

/************************************
* End of file
************************************/
