
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
#include <cstring>
#include <memory>

#include "mesh.h"
#include "material.h"
#include "Loader/objLoader.h"

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

		// 1. OBJ を読む (CPU 側)
		ObjLoader loader;
		MeshPtr mesh(loader.LoadModel(filePath));
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
		std::vector<RHI::TextureHandle> materialTextures(mesh->Materials.size());
		{
			std::vector<const wchar_t*> paths;
			std::vector<size_t>         owners;     // paths[i] が何番目のマテリアルのものか
			for (size_t m = 0; m < mesh->Materials.size(); ++m)
			{
				const Material* material = mesh->Materials[m];
				if (material && material->UseTexture && !material->TexturePath.empty())
				{
					paths.push_back(material->TexturePath.c_str());
					owners.push_back(m);
				}
			}

			if (!paths.empty())
			{
				std::vector<RHI::TextureHandle> loaded(paths.size());
				_rhi->LoadTextures(paths.data(), static_cast<uint32_t>(paths.size()), loaded.data());
				for (size_t i = 0; i < loaded.size(); ++i)
				{
					materialTextures[owners[i]] = loaded[i];
				}
			}
		}

		// 4. サブメッシュごとにインデックスを登録する (サブメッシュ i はマテリアル i を使う。OBJ の usemtl の順番)
		//    描画オブジェクトはまだ作らない (以前は読み込みと同時に原点に 1 個置いていた)
		ModelResource model;
		for (size_t i = 0; i < mesh->Indecies.size(); ++i)
		{
			const auto& indices = mesh->Indecies[i];
			const RHI::IndexRange range = _rhi->UploadIndices(indices.data(), static_cast<uint32_t>(indices.size()));
			if (!range.IsValid())
			{
				Core::Logger::Log("[SceneRenderer] Failed upload indices.");
				continue;
			}

			RHI::DrawItem item;
			item.BaseVertex = vertices.BaseVertex;
			item.FirstIndex = range.FirstIndex;
			item.IndexCount = range.Count;
			model.SubMeshes.push_back(item);
			model.Textures.push_back(i < materialTextures.size() ? materialTextures[i].Index : RHI::TextureHandle::DEFAULT);
		}
		if (model.SubMeshes.empty())
		{
			return INVALID_HANDLE;
		}

		if (outSubMeshCount) *outSubMeshCount = static_cast<uint32_t>(model.SubMeshes.size());
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

		const ModelResource& resource = _models[model];
		for (uint32_t n = 0; n < count; ++n)
		{
			// サブメッシュごとに RHI のオブジェクトを作る (初期値は原点・そのサブメッシュのテクスチャ)
			RenderObject object;
			object.Model = model;
			object.Objects.reserve(resource.SubMeshes.size());
			for (size_t i = 0; i < resource.SubMeshes.size(); ++i)
			{
				const RHI::ObjectHandle handle = _rhi->CreateObject();
				if (!handle.IsValid()) break;

				RHI::ObjectConstants constants;
				::DirectX::XMStoreFloat4x4(&constants.World, ::DirectX::XMMatrixIdentity());
				constants.TextureIndex = resource.Textures[i];
				_rhi->UpdateObject(handle, constants);
				object.Objects.push_back(handle);
			}
			if (object.Objects.size() != resource.SubMeshes.size())
			{
				// RHI のオブジェクトの上限 (作った分は戻す)
				for (const auto& handle : object.Objects) _rhi->DestroyObject(handle);
				Core::Logger::Log("[SceneRenderer] Object limit reached.");
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

			for (const auto& object : _renderObjects[handle].Objects) _rhi->DestroyObject(object);
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
			RHI::ObjectConstants constants;
			std::memcpy(&constants.World, matrices + static_cast<size_t>(n) * 16, sizeof(constants.World));

			const ModelResource& resource = _models[object.Model];
			for (size_t i = 0; i < object.Objects.size(); ++i)
			{
				constants.TextureIndex = resource.Textures[i];
				_rhi->UpdateObject(object.Objects[i], constants);
			}
		}
	}

	void SceneRenderer::rebuildDrawList()
	{
		// 描画オブジェクトの番号の順に並べる (作った順に近い順 = インスタンシングと GPU のまとまりが良い)
		_drawItems.clear();
		_drawItems.reserve(_aliveRenderObjects * 2);
		for (const auto& object : _renderObjects)
		{
			if (object.Model == INVALID_HANDLE) continue;
			const ModelResource& resource = _models[object.Model];
			for (size_t i = 0; i < object.Objects.size(); ++i)
			{
				RHI::DrawItem item = resource.SubMeshes[i];
				item.Object = object.Objects[i];
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

	bool SceneRenderer::Render()
	{
		if (!IsInitialized()) return false;

		// 描画リストは描画オブジェクトが増減した時だけ作り直す (位置が変わっただけなら同じリストのまま)
		// RHI は描画リストが前回と同じなら、GPU 用の描画データも書き直さない
		if (_drawListDirty) rebuildDrawList();
		return _rhi->Render(_drawItems.data(), static_cast<uint32_t>(_drawItems.size()));
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
