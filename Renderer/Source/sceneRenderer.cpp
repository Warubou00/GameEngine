
/***********************************************************************************
* @file    sceneRenderer.cpp
* @brief   シーンの描画 (モデル・カメラ・描画リストの管理)
* @details
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "sceneRenderer.h"

#include <algorithm>
#include <memory>

#include "mesh.h"
#include "material.h"
#include "Loader/objLoader.h"
#include "WVP/viewMatrix.h"

#include "../../Core/Source/Log/logger.h"
#include "../../Core/Source/Log/scopedTimer.h"

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
		_drawItems.clear();
		_hasCamera = false;
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

	bool SceneRenderer::LoadModel(const std::wstring& filePath)
	{
		if (!IsInitialized()) return false;

		// 1. OBJ を読む (CPU 側)
		ObjLoader loader;
		MeshPtr mesh(loader.LoadModel(filePath));
		if (!mesh)
		{
			Core::Logger::Log(L"[SceneRenderer] Failed load model.");
			Core::Logger::Log(filePath.c_str());
			return false;
		}

		// 2. 頂点 (メッシュ全体で 1 回)
		const RHI::VertexRange vertices = _rhi->UploadVertices(mesh->Vertices.data(), static_cast<uint32_t>(mesh->Vertices.size()));
		if (!vertices.IsValid())
		{
			Core::Logger::Log("[SceneRenderer] Failed upload vertices.");
			return false;
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

		_modelItems.clear();
		_modelTextures.clear();

		// 4. サブメッシュごとにインデックスを登録し、描画オブジェクトと DrawItem を作る
		//    (サブメッシュ i はマテリアル i を使う。OBJ の usemtl の順番)
		for (size_t i = 0; i < mesh->Indecies.size(); ++i)
		{
			const auto& indices = mesh->Indecies[i];
			const RHI::IndexRange range = _rhi->UploadIndices(indices.data(), static_cast<uint32_t>(indices.size()));
			if (!range.IsValid())
			{
				Core::Logger::Log("[SceneRenderer] Failed upload indices.");
				continue;
			}

			const RHI::ObjectHandle object = _rhi->CreateObject();
			if (!object.IsValid())
			{
				continue;
			}

			RHI::ObjectConstants constants;
			::DirectX::XMStoreFloat4x4(&constants.World, ::DirectX::XMMatrixIdentity());   // 今は原点に置くだけ
			constants.TextureIndex = (i < materialTextures.size()) ? materialTextures[i].Index : RHI::TextureHandle::DEFAULT;
			_rhi->UpdateObject(object, constants);

			RHI::DrawItem item;
			item.BaseVertex = vertices.BaseVertex;
			item.FirstIndex = range.FirstIndex;
			item.IndexCount = range.Count;
			item.Object     = object;
			_drawItems.push_back(item);
			_modelItems.push_back(item);
			_modelTextures.push_back(constants.TextureIndex);
		}

		// プロファイラーに表示 (描画リストの数)
		_rhi->ReportProfilerValue("Draw items", static_cast<double>(_drawItems.size()));

		// 5. CPU 側のメッシュはここで解放される (GPU へのコピーは RHI のアップロード領域に済んでいる)
		return true;
	}

	void SceneRenderer::SpawnTestGrid(uint32_t count, float spacing)
	{
		if (!IsInitialized() || _modelItems.empty() || count == 0) return;

		// 正方形に近い格子 (side x side) に並べる。原点が中心になるようにずらす
		uint32_t side = 1;
		while (side * side < count) ++side;
		const float half = (side - 1) * spacing * 0.5f;

		uint32_t spawned = 0;
		for (uint32_t i = 0; i < count; ++i)
		{
			const float x = (i % side) * spacing - half;
			const float z = (i / side) * spacing - half;

			for (size_t j = 0; j < _modelItems.size(); ++j)
			{
				const RHI::ObjectHandle object = _rhi->CreateObject();
				if (!object.IsValid())
				{
					// オブジェクトの上限 (RHI 側で 1 フレーム 32,768 個)
					Core::Logger::Log("[SceneRenderer] Object limit reached.");
					i = count;
					break;
				}

				RHI::ObjectConstants constants;
				::DirectX::XMStoreFloat4x4(&constants.World, ::DirectX::XMMatrixTranslation(x, 0.0f, z));
				constants.TextureIndex = _modelTextures[j];
				_rhi->UpdateObject(object, constants);

				RHI::DrawItem item = _modelItems[j];   // 頂点 / インデックスは同じものを使う
				item.Object = object;
				_drawItems.push_back(item);
			}
			++spawned;
		}

		// 全体が見えるようにカメラを引く (斜め上から原点を見る)
		const float distance = (std::max)(10.0f, side * spacing * 0.9f);
		Camera camera;
		camera.Position = Core::Vector3(0.0f, distance * 0.8f, -distance);
		camera.Target   = Core::Vector3(0.0f, 0.0f, 0.0f);
		camera.Up       = Core::Vector3(0.0f, 1.0f, 0.0f);
		SetCamera(camera);

		_rhi->ReportProfilerValue("Test grid (models)", static_cast<double>(spawned));
		_rhi->ReportProfilerValue("Draw items", static_cast<double>(_drawItems.size()));
	}

	void SceneRenderer::SetIndirectDrawEnabled(bool enabled)
	{
		if (_rhi) _rhi->SetIndirectDrawEnabled(enabled);
	}

	void SceneRenderer::SetCamera(const Camera& camera, Projection::ProjectionType type)
	{
		_camera         = camera;
		_projectionType = type;
		_hasCamera      = true;
		updateViewProjection();
	}

	bool SceneRenderer::Render()
	{
		if (!IsInitialized()) return false;
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

		View       view;
		Projection projection;

		// どちらも転置しない普通の行列 (転置は RHI の中で行う)
		const auto viewMatrix = toFloat4x4(view.GetViewMatrix(_camera));
		const auto projMatrix = toFloat4x4(projection.GetProjectionMatrix(
			static_cast<float>(_rhi->GetWidth()), static_cast<float>(_rhi->GetHeight()), 1.0f, 10000.0f, _projectionType));

		_rhi->SetViewProjection(viewMatrix, projMatrix);
	}
}

/************************************
* End of file
************************************/
