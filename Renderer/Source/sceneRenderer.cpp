
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
		_freeModels.clear();
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

	// ファイルを読んで GPU へ登録し、使った物 (頂点 / インデックス / マテリアル / テクスチャ) を model に覚える (ED5)
	// (以前は LoadModel の中身。読み直しでも使うので分けた)
	bool SceneRenderer::loadModelResource(const std::wstring& filePath, ModelResource& model)
	{

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
			return false;
		}
		MeshPtr mesh(loader->LoadModel(filePath));
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
		model.Vertices = vertices;

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
				for (const auto& texture : loaded) if (texture.Index != RHI::TextureHandle::DEFAULT) model.Textures.push_back(texture);
				model.TexturePaths = paths;
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
			model.IndexRanges.push_back(range);

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
			model.Materials.push_back(item.Material);
			model.SubMeshes.push_back(item);
			model.SubMeshMaterials.push_back(subMeshMaterialOf(desc, material && desc.Texture.Index != RHI::TextureHandle::DEFAULT ? resolveTexturePath(material->TexturePath) : std::wstring(),
			                                                   material ? material->Name : std::wstring()));   // v1.5
		}
		if (model.SubMeshes.empty())
		{
			releaseModelResource(model);
			return false;
		}

		// 衝突判定のメッシュ用に、位置とインデックスだけ CPU 側に残す (頂点 12byte + インデックス 4byte。色や UV は捨てる)
		model.Positions.reserve(mesh->Vertices.size() * 3);
		for (const auto& v : mesh->Vertices)
		{
			model.Positions.push_back(v.Position._x);
			model.Positions.push_back(v.Position._y);
			model.Positions.push_back(v.Position._z);
		}
		for (const auto& indices : mesh->Indecies) model.Indices.insert(model.Indices.end(), indices.begin(), indices.end());

		model.Path = filePath;
		// 5. CPU 側のメッシュはここで解放される (GPU へのコピーは RHI のアップロード領域に済んでいる)
		return true;
	}

	uint32_t SceneRenderer::LoadModel(const std::wstring& filePath, uint32_t* outSubMeshCount)
	{
		if (outSubMeshCount) *outSubMeshCount = 0;
		if (!IsInitialized()) return INVALID_HANDLE;
		ModelResource model;
		if (!loadModelResource(filePath, model)) return INVALID_HANDLE;
		if (outSubMeshCount) *outSubMeshCount = static_cast<uint32_t>(model.SubMeshes.size());

		// 解放したモデルの番号があれば使い回す (ED5)
		if (!_freeModels.empty())
		{
			const uint32_t handle = _freeModels.back();
			_freeModels.pop_back();
			_models[handle] = std::move(model);
			return handle;
		}
		_models.push_back(std::move(model));
		return static_cast<uint32_t>(_models.size() - 1);
	}

	// 頂点とインデックスの配列からモデルを作る (SH1)。ファイルを読まない以外は loadModelResource と同じ登録をする
	uint32_t SceneRenderer::CreateMesh(const RHI::Vertex* vertices, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount, const std::wstring& name)
	{
		if (!IsInitialized() || !vertices || !indices || vertexCount == 0 || indexCount < 3 || name.empty()) return INVALID_HANDLE;
		for (uint32_t i = 0; i < indexCount; ++i)
		{
			if (indices[i] >= vertexCount)
			{
				Core::Logger::Log("[SceneRenderer] CreateMesh : index out of range.");
				return INVALID_HANDLE;
			}
		}
		ModelResource model;
		model.Vertices = _rhi->UploadVertices(vertices, vertexCount);
		if (!model.Vertices.IsValid())
		{
			Core::Logger::Log("[SceneRenderer] CreateMesh : failed upload vertices.");
			return INVALID_HANDLE;
		}
		const RHI::IndexRange range = _rhi->UploadIndices(indices, indexCount);
		if (!range.IsValid())
		{
			releaseModelResource(model);
			Core::Logger::Log("[SceneRenderer] CreateMesh : failed upload indices.");
			return INVALID_HANDLE;
		}
		model.IndexRanges.push_back(range);

		RHI::MaterialDesc desc;   // 白 / 不透明 / 片面
		desc.Metallic  = 0.0f;
		desc.Roughness = 0.6f;
		RHI::DrawItem item;
		item.BaseVertex = model.Vertices.BaseVertex;
		item.FirstIndex = range.FirstIndex;
		item.IndexCount = range.Count;
		item.Material   = _rhi->CreateMaterial(desc);
		model.Materials.push_back(item.Material);
		model.SubMeshes.push_back(item);
		model.SubMeshMaterials.push_back(subMeshMaterialOf(desc, std::wstring(), std::wstring()));   // v1.5

		model.Positions.reserve(static_cast<size_t>(vertexCount) * 3);
		for (uint32_t i = 0; i < vertexCount; ++i)
		{
			model.Positions.push_back(vertices[i].Position._x);
			model.Positions.push_back(vertices[i].Position._y);
			model.Positions.push_back(vertices[i].Position._z);
		}
		model.Indices.assign(indices, indices + indexCount);
		model.Path = name;   // 空でない = 使っている番号 (ファイルではないので ReloadModel は読めずに前のまま)

		if (!_freeModels.empty())
		{
			const uint32_t handle = _freeModels.back();
			_freeModels.pop_back();
			_models[handle] = std::move(model);
			return handle;
		}
		_models.push_back(std::move(model));
		return static_cast<uint32_t>(_models.size() - 1);
	}

	// AN2 : C# が FBX から作った形 (サブメッシュとマテリアル付き) を登録する。loadModelResource と同じ並びで作る
	uint32_t SceneRenderer::CreateModel(const RHI::Vertex* vertices, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount,
	                                    const ModelSubMeshDesc* subMeshes, uint32_t subMeshCount, const wchar_t* const* texturePaths,
	                                    const std::wstring& name, uint32_t replace, uint32_t* outSubMeshCount)
	{
		if (outSubMeshCount) *outSubMeshCount = 0;
		if (!IsInitialized() || !vertices || !indices || !subMeshes || vertexCount == 0 || indexCount < 3 || subMeshCount == 0 || name.empty()) return INVALID_HANDLE;
		for (uint32_t i = 0; i < indexCount; ++i)
		{
			if (indices[i] >= vertexCount)
			{
				Core::Logger::Log("[SceneRenderer] CreateModel : index out of range.");
				return INVALID_HANDLE;
			}
		}
		for (uint32_t s = 0; s < subMeshCount; ++s)
		{
			if (subMeshes[s].FirstIndex + subMeshes[s].IndexCount > indexCount)
			{
				Core::Logger::Log("[SceneRenderer] CreateModel : sub mesh range out of range.");
				return INVALID_HANDLE;
			}
		}
		ModelResource model;
		model.Vertices = _rhi->UploadVertices(vertices, vertexCount);
		if (!model.Vertices.IsValid())
		{
			Core::Logger::Log("[SceneRenderer] CreateModel : failed upload vertices.");
			return INVALID_HANDLE;
		}
		// テクスチャ : 同じパスは 1 回だけ (まとめて読む = RHI の中で並列にデコード)
		std::vector<RHI::TextureHandle> subMeshTextures(subMeshCount);
		if (texturePaths)
		{
			std::vector<std::wstring> paths;
			std::vector<size_t> owners(subMeshCount, SIZE_MAX);
			for (uint32_t s = 0; s < subMeshCount; ++s)
			{
				if (!texturePaths[s] || !texturePaths[s][0]) continue;
				const std::wstring path = resolveTexturePath(texturePaths[s]);
				auto found = std::find(paths.begin(), paths.end(), path);
				if (found == paths.end()) { paths.push_back(path); found = paths.end() - 1; }
				owners[s] = static_cast<size_t>(found - paths.begin());
			}
			if (!paths.empty())
			{
				std::vector<const wchar_t*> pointers;
				for (const auto& path : paths) pointers.push_back(path.c_str());
				std::vector<RHI::TextureHandle> loaded(paths.size());
				_rhi->LoadTextures(pointers.data(), static_cast<uint32_t>(pointers.size()), loaded.data());
				for (const auto& texture : loaded) if (texture.Index != RHI::TextureHandle::DEFAULT) model.Textures.push_back(texture);
				model.TexturePaths = paths;
				for (uint32_t s = 0; s < subMeshCount; ++s) if (owners[s] != SIZE_MAX) subMeshTextures[s] = loaded[owners[s]];
			}
		}
		for (uint32_t s = 0; s < subMeshCount; ++s)
		{
			const ModelSubMeshDesc& sub = subMeshes[s];
			if (sub.IndexCount == 0) continue;
			const RHI::IndexRange range = _rhi->UploadIndices(indices + sub.FirstIndex, sub.IndexCount);
			if (!range.IsValid())
			{
				Core::Logger::Log("[SceneRenderer] CreateModel : failed upload indices.");
				continue;
			}
			model.IndexRanges.push_back(range);
			RHI::MaterialDesc desc;
			const bool textured = subMeshTextures[s].Index != RHI::TextureHandle::DEFAULT;
			desc.Texture = textured ? subMeshTextures[s] : RHI::TextureHandle();
			for (int c = 0; c < 4; ++c) desc.Color[c] = sub.Color[c];
			desc.Metallic    = sub.Metallic;
			desc.Roughness   = sub.Roughness;
			desc.Blend       = (sub.Flags & 1u) ? RHI::BlendMode::AlphaBlend : RHI::BlendMode::Opaque;
			desc.DoubleSided = (sub.Flags & 2u) != 0;
			RHI::DrawItem item;
			item.BaseVertex = model.Vertices.BaseVertex;
			item.FirstIndex = range.FirstIndex;
			item.IndexCount = range.Count;
			item.Material   = _rhi->CreateMaterial(desc);
			model.Materials.push_back(item.Material);
			model.SubMeshes.push_back(item);
			model.SubMeshMaterials.push_back(subMeshMaterialOf(desc, textured && texturePaths && texturePaths[s] ? resolveTexturePath(texturePaths[s]) : std::wstring(), std::wstring()));   // v1.5
		}
		if (model.SubMeshes.empty())
		{
			releaseModelResource(model);
			return INVALID_HANDLE;
		}
		model.Positions.reserve(static_cast<size_t>(vertexCount) * 3);
		for (uint32_t i = 0; i < vertexCount; ++i)
		{
			model.Positions.push_back(vertices[i].Position._x);
			model.Positions.push_back(vertices[i].Position._y);
			model.Positions.push_back(vertices[i].Position._z);
		}
		model.Indices.assign(indices, indices + indexCount);
		model.Path = name;
		const uint32_t count = static_cast<uint32_t>(model.SubMeshes.size());

		// 入れ替え (読み直し) : 同じ番号のまま、前の物を GPU を待ってから返す
		if (replace != INVALID_HANDLE && replace < _models.size() && !_models[replace].Path.empty())
		{
			// AN4 : 前の形の曲げる物を消す (骨と重みは新しい形で付け直してもらう = SetModelSkin)
			for (auto& object : _renderObjects)
			{
				if (object.Model == replace && object.Skin.IsValid()) { _rhi->DestroySkinInstance(object.Skin); object.Skin = RHI::SkinHandle(); }
			}
			ModelResource old = std::move(_models[replace]);
			_models[replace] = std::move(model);
			releaseModelResource(old);
			_drawListDirty = true;
			if (outSubMeshCount) *outSubMeshCount = count;
			return replace;
		}
		if (outSubMeshCount) *outSubMeshCount = count;
		if (!_freeModels.empty())
		{
			const uint32_t handle = _freeModels.back();
			_freeModels.pop_back();
			_models[handle] = std::move(model);
			return handle;
		}
		_models.push_back(std::move(model));
		return static_cast<uint32_t>(_models.size() - 1);
	}

	// ==============================================================
	// ED5 : 読み直し / 解放 / マテリアル
	// ==============================================================

	void SceneRenderer::releaseModelResource(ModelResource& model)
	{
		if (model.Skin.IsValid()) _rhi->ReleaseSkin(model.Skin);   // AN4
		std::vector<RHI::VertexRange> vertices;
		if (model.Vertices.IsValid()) vertices.push_back(model.Vertices);
		_rhi->ReleaseResources(vertices.data(), static_cast<uint32_t>(vertices.size()),
		                       model.IndexRanges.data(), static_cast<uint32_t>(model.IndexRanges.size()),
		                       model.Materials.data(), static_cast<uint32_t>(model.Materials.size()),
		                       model.Textures.data(), static_cast<uint32_t>(model.Textures.size()));
		model = ModelResource();
	}

	bool SceneRenderer::ReloadModel(uint32_t handle, uint32_t* outSubMeshCount)
	{
		if (outSubMeshCount) *outSubMeshCount = 0;
		if (!IsInitialized() || handle >= _models.size() || _models[handle].Path.empty()) return false;
		// 新しい物を先に読む (読めなければ前の物のまま。書きかけのファイルで消えないように)
		ModelResource fresh;
		if (!loadModelResource(_models[handle].Path, fresh)) return false;
		ModelResource old = std::move(_models[handle]);
		_models[handle] = std::move(fresh);
		releaseModelResource(old);   // GPU を待ってから返す
		_drawListDirty = true;       // 描画オブジェクトは同じ番号のまま、新しいサブメッシュで描く
		if (outSubMeshCount) *outSubMeshCount = static_cast<uint32_t>(_models[handle].SubMeshes.size());
		return true;
	}

	bool SceneRenderer::UnloadModel(uint32_t handle)
	{
		if (!IsInitialized() || handle >= _models.size() || _models[handle].Path.empty()) return false;
		for (const auto& object : _renderObjects)
		{
			if (object.Model == handle) return false;   // まだ置いてある
		}
		releaseModelResource(_models[handle]);
		_freeModels.push_back(handle);
		return true;
	}

	uint32_t SceneRenderer::ReloadModelsUsingTexture(const std::wstring& texturePath)
	{
		if (!IsInitialized()) return 0;
		std::error_code error;
		const auto target = std::filesystem::weakly_canonical(texturePath, error);
		uint32_t count = 0;
		for (uint32_t handle = 0; handle < _models.size(); ++handle)
		{
			bool uses = false;
			for (const auto& path : _models[handle].TexturePaths)
			{
				std::error_code e;
				if (std::filesystem::weakly_canonical(path, e) == target) { uses = true; break; }
			}
			if (uses && ReloadModel(handle, nullptr)) count++;
		}
		return count;
	}

	uint32_t SceneRenderer::LoadTexture(const std::wstring& filePath)
	{
		if (!IsInitialized()) return RHI::TextureHandle::DEFAULT;
		const std::wstring path = resolveTexturePath(filePath);
		const wchar_t* pointer = path.c_str();
		RHI::TextureHandle handle;
		_rhi->LoadTextures(&pointer, 1, &handle);
		return handle.Index;
	}

	void SceneRenderer::ReleaseTexture(uint32_t texture)
	{
		if (!IsInitialized() || texture == RHI::TextureHandle::DEFAULT) return;
		RHI::TextureHandle handle;
		handle.Index = texture;
		_rhi->ReleaseResources(nullptr, 0, nullptr, 0, nullptr, 0, &handle, 1);
	}

	uint32_t SceneRenderer::CreateMaterial(const RHI::MaterialDesc& desc)
	{
		return IsInitialized() ? _rhi->CreateMaterial(desc).Index : RHI::MaterialHandle::DEFAULT;
	}

	void SceneRenderer::UpdateMaterial(uint32_t material, const RHI::MaterialDesc& desc)
	{
		if (!IsInitialized()) return;
		RHI::MaterialHandle handle;
		handle.Index = material;
		_rhi->UpdateMaterial(handle, desc);
	}

	void SceneRenderer::DestroyMaterial(uint32_t material)
	{
		if (!IsInitialized() || material == RHI::MaterialHandle::DEFAULT) return;
		// まだ使っている描画オブジェクトがあれば、モデルのマテリアルに戻す
		for (auto& object : _renderObjects)
		{
			if (object.MaterialOverride == material) { object.MaterialOverride = INVALID_HANDLE; _drawListDirty = true; }
			for (auto& m : object.MaterialOverrides) if (m == material) { m = INVALID_HANDLE; _drawListDirty = true; }   // v1.5
		}
		RHI::MaterialHandle handle;
		handle.Index = material;
		_rhi->ReleaseResources(nullptr, 0, nullptr, 0, &handle, 1, nullptr, 0);
	}

	void SceneRenderer::SetRenderObjectMaterial(uint32_t handle, uint32_t material)
	{
		if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE) return;
		if (_renderObjects[handle].MaterialOverride == material) return;
		_renderObjects[handle].MaterialOverride = material;
		_drawListDirty = true;
	}

	uint64_t SceneRenderer::GetMeshBytesUsed() const
	{
		return IsInitialized() ? _rhi->GetMeshBytesUsed() : 0;
	}

	uint32_t SceneRenderer::GetLoadedModelCount() const
	{
		uint32_t count = 0;
		for (const auto& model : _models) if (!model.Path.empty()) count++;
		return count;
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
			if (_renderObjects[handle].Skin.IsValid()) _rhi->DestroySkinInstance(_renderObjects[handle].Skin);   // AN4
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
			RenderObject& object = _renderObjects[handle];
			if (object.Model == INVALID_HANDLE) continue;

			// C# の Matrix4x4 と XMFLOAT4X4 は同じ並び (行優先・行ベクトル) なのでそのまま写す
			// (オブジェクトは全サブメッシュで共有しているので 1 回で済む。以前はサブメッシュの数だけ書いていた)
			// v1.5 : 印 (影 / レイヤー) はそのまま (覚えている物を使う)
			std::memcpy(&object.Constants.World, matrices + static_cast<size_t>(n) * 16, sizeof(object.Constants.World));
			_rhi->UpdateObject(object.Object, object.Constants);
		}
	}

	// ==============================================================
	// AN4 : GPU スキニング
	// ==============================================================

	bool SceneRenderer::SetModelSkin(uint32_t handle, const RHI::SkinVertex* vertices, uint32_t count, uint32_t boneCount)
	{
		if (!IsInitialized() || handle >= _models.size() || _models[handle].Path.empty() || !vertices || boneCount == 0) return false;
		ModelResource& model = _models[handle];
		if (count != model.Vertices.Count)
		{
			Core::Logger::Log("[SceneRenderer] SetModelSkin : the skin count does not match the vertex count.");
			return false;
		}
		// 置いてある曲げる物を消してから付け替える (骨の数が変わるかもしれないので作り直す)
		std::vector<uint32_t> skinnedObjects;
		for (uint32_t i = 0; i < _renderObjects.size(); ++i)
		{
			auto& object = _renderObjects[i];
			if (object.Model != handle || !object.Skin.IsValid()) continue;
			_rhi->DestroySkinInstance(object.Skin);
			object.Skin = RHI::SkinHandle();
			skinnedObjects.push_back(i);
		}
		if (model.Skin.IsValid()) _rhi->ReleaseSkin(model.Skin);
		model.Skin      = _rhi->UploadSkin(vertices, count);
		model.SkinBones = model.Skin.IsValid() ? boneCount : 0;
		if (!skinnedObjects.empty()) SetObjectsSkinned(skinnedObjects.data(), static_cast<uint32_t>(skinnedObjects.size()), true);
		_drawListDirty = true;
		return model.Skin.IsValid();
	}

	void SceneRenderer::SetObjectsSkinned(const uint32_t* handles, uint32_t count, bool skinned)
	{
		if (!handles || !IsInitialized()) return;
		for (uint32_t n = 0; n < count; ++n)
		{
			const uint32_t handle = handles[n];
			if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE) continue;
			RenderObject& object = _renderObjects[handle];
			const ModelResource& model = _models[object.Model];
			if (skinned && !object.Skin.IsValid() && model.Skin.IsValid())
			{
				object.Skin = _rhi->CreateSkinInstance(model.Vertices, model.Skin, model.SkinBones);
				_drawListDirty = true;
			}
			else if (!skinned && object.Skin.IsValid())
			{
				_rhi->DestroySkinInstance(object.Skin);
				object.Skin = RHI::SkinHandle();
				_drawListDirty = true;
			}
		}
	}

	// ==============================================================
	// プレビューの窓 (AN5)
	// ==============================================================

	void SceneRenderer::SetObjectsView(const uint32_t* handles, uint32_t count, uint32_t view)
	{
		if (!handles || !IsInitialized() || view >= RHI::MAX_PREVIEW_VIEWS) return;
		for (uint32_t n = 0; n < count; ++n)
		{
			const uint32_t handle = handles[n];
			if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE) continue;
			if (_renderObjects[handle].View == view) continue;
			_renderObjects[handle].View = view;
			_drawListDirty = true;
		}
	}

	void SceneRenderer::SetPreviewFrame(uint32_t view, const RHI::PreviewFrame& frame, const RHI::DebugLineVertex* lines, uint32_t lineVertexCount,
	                                    uint32_t depthLineVertexCount)
	{
		if (_rhi) _rhi->SetPreviewFrame(view, frame, lines, lineVertexCount, depthLineVertexCount);
	}

	uint32_t SceneRenderer::GetDrawItemCount(uint32_t view)
	{
		if (_drawListDirty && IsInitialized()) rebuildDrawList();
		if (view == 0) return static_cast<uint32_t>(_drawItems.size());
		return view < RHI::MAX_PREVIEW_VIEWS ? static_cast<uint32_t>(_previewItems[view].size()) : 0;
	}

	uint32_t SceneRenderer::GetRenderTargetTexture(uint32_t target)const
	{
		RHI::RenderTargetHandle handle;
		handle.Index = target;
		return _rhi ? _rhi->GetRenderTargetTexture(handle) : 0xFFFFFFFFu;
	}

	void SceneRenderer::SetSkinPoses(const uint32_t* handles, uint32_t count, const float* matrices, const uint32_t* boneCounts, const float* spheres)
	{
		if (!handles || !matrices || !boneCounts || !IsInitialized()) return;
		size_t offset = 0;
		for (uint32_t n = 0; n < count; ++n)
		{
			const uint32_t handle = handles[n];
			const float* m = matrices + offset * 12;
			offset += boneCounts[n];
			if (handle >= _renderObjects.size() || !_renderObjects[handle].Skin.IsValid()) continue;
			_rhi->SetSkinPose(_renderObjects[handle].Skin, m, boneCounts[n]);
			if (spheres) _rhi->SetSkinBounds(_renderObjects[handle].Skin, spheres + static_cast<size_t>(n) * 4);
		}
	}

	void SceneRenderer::GetSkinStats(uint32_t* outInstances, uint32_t* outVertices, float* outGpuMs)const
	{
		if (_rhi) _rhi->GetSkinStats(outInstances, outVertices, outGpuMs);
	}

	void SceneRenderer::RequestSkinReadback(uint32_t handle)
	{
		if (IsInitialized() && handle < _renderObjects.size() && _renderObjects[handle].Skin.IsValid()) _rhi->RequestSkinReadback(_renderObjects[handle].Skin);
	}

	uint32_t SceneRenderer::ReadSkinReadback(RHI::Vertex* outVertices, uint32_t maxCount)
	{
		return IsInitialized() ? _rhi->ReadSkinReadback(outVertices, maxCount) : 0;
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
		// AN5 : プレビューの窓の物は窓ごとのリストへ (シーンには描かない)。以前は全部シーンのリストだった
		bool previewUsed[RHI::MAX_PREVIEW_VIEWS] = {};
		for (uint32_t v = 1; v < RHI::MAX_PREVIEW_VIEWS; ++v) { previewUsed[v] = !_previewItems[v].empty(); _previewItems[v].clear(); }
		for (const auto& object : _renderObjects)
		{
			if (object.Model == INVALID_HANDLE) continue;
			auto& list = object.View == 0 ? _drawItems : _previewItems[object.View];
			// AN4 : 曲げる物はその物専用の頂点の場所 (インデックスは同じ)
			const RHI::VertexRange skinned = object.Skin.IsValid() ? _rhi->GetSkinOutput(object.Skin) : RHI::VertexRange();
			const auto& subMeshes = _models[object.Model].SubMeshes;
			for (size_t s = 0; s < subMeshes.size(); ++s)
			{
				RHI::DrawItem item = subMeshes[s];
				item.Object = object.Object;
				if (skinned.IsValid()) item.BaseVertex = skinned.BaseVertex;
				if (object.MaterialOverride != INVALID_HANDLE) item.Material.Index = object.MaterialOverride;   // ED5 : マテリアルのアセット
				// v1.5 : サブメッシュごとのマテリアル (1 Mesh / 1 Material。MeshRenderer.Materials[s])
				if (s < object.MaterialOverrides.size() && object.MaterialOverrides[s] != INVALID_HANDLE) item.Material.Index = object.MaterialOverrides[s];
				list.push_back(item);
			}
		}
		for (uint32_t v = 1; v < RHI::MAX_PREVIEW_VIEWS; ++v)
		{
			if (previewUsed[v] || !_previewItems[v].empty())
				_rhi->SetPreviewItems(v, _previewItems[v].data(), static_cast<uint32_t>(_previewItems[v].size()));
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
		SetCameras(&camera, 1);
	}

	void SceneRenderer::SetCameras(const CameraDesc* cameras, uint32_t count)
	{
		if (!cameras || count == 0) return;
		_cameras.assign(cameras, cameras + (std::min)(count, RHI::MAX_CAMERAS));
		_camera    = _cameras[0];
		_hasCamera = true;
		updateViewProjection();
	}

	void SceneRenderer::SetRenderObjectFlags(uint32_t handle, uint32_t flags, uint32_t layerMask)
	{
		if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE || !IsInitialized()) return;
		RenderObject& object = _renderObjects[handle];
		if (object.Constants.Flags == flags && object.Constants.LayerMask == layerMask) return;
		object.Constants.Flags     = flags;
		object.Constants.LayerMask = layerMask;
		_rhi->UpdateObject(object.Object, object.Constants);
	}

	void SceneRenderer::SetRenderObjectMaterials(uint32_t handle, const uint32_t* materials, uint32_t count)
	{
		if (handle >= _renderObjects.size() || _renderObjects[handle].Model == INVALID_HANDLE) return;
		std::vector<uint32_t> overrides(materials, materials + (materials ? count : 0));
		if (_renderObjects[handle].MaterialOverrides == overrides) return;
		_renderObjects[handle].MaterialOverrides = std::move(overrides);
		_drawListDirty = true;
	}

	bool SceneRenderer::Render(const RHI::RenderCommand* commands, uint32_t commandCount)
	{
		if (!IsInitialized()) return false;

		// 描画リストは描画オブジェクトが増減した時だけ作り直す (位置が変わっただけなら同じリストのまま)
		// RHI は描画リストが前回と同じなら、GPU 用の描画データも書き直さない
		if (_drawListDirty) rebuildDrawList();
		return _rhi->Render(_drawItems.data(), static_cast<uint32_t>(_drawItems.size()), commands, commandCount);
	}

	// v1.5 : 読み込んだマテリアルを覚える形にする
	SceneRenderer::ModelResource::SubMeshMaterial SceneRenderer::subMeshMaterialOf(const RHI::MaterialDesc& desc, const std::wstring& texture, const std::wstring& name)
	{
		ModelResource::SubMeshMaterial result;
		for (int c = 0; c < 4; ++c) result.Info.Color[c] = desc.Color[c];
		result.Info.Metallic  = desc.Metallic;
		result.Info.Roughness = desc.Roughness;
		result.Info.Flags     = (desc.Blend == RHI::BlendMode::AlphaBlend ? 1u : 0u) | (desc.DoubleSided ? 2u : 0u);
		result.Texture = texture;
		result.Name    = name;
		return result;
	}

	bool SceneRenderer::GetModelMaterialInfo(uint32_t model, uint32_t subMesh, ModelMaterialInfo* info, std::wstring& texture, std::wstring& name) const
	{
		if (model >= _models.size()) return false;
		const auto& materials = _models[model].SubMeshMaterials;
		if (subMesh >= materials.size()) return false;
		if (info) *info = materials[subMesh].Info;
		texture = materials[subMesh].Texture;
		name    = materials[subMesh].Name;
		return true;
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

	bool SceneRenderer::SetRenderSize(uint32_t width, uint32_t height)
	{
		if (!IsInitialized() || !_rhi->SetRenderSize(width, height)) return false;
		if (_hasCamera) updateViewProjection();   // アスペクト比が変わる
		return true;
	}

	void SceneRenderer::SetUIDrawData(const RHI::UIVertex* vertices, uint32_t vertexCount, const uint16_t* indices, uint32_t indexCount,
	                                  const RHI::UIDrawCommand* commands, uint32_t commandCount)
	{
		if (IsInitialized()) _rhi->SetUIDrawData(vertices, vertexCount, indices, indexCount, commands, commandCount);
	}

	uint32_t SceneRenderer::CreateUITexture(uint32_t width, uint32_t height, const uint8_t* rgba)
	{
		return IsInitialized() ? _rhi->CreateUITexture(width, height, rgba) : 0xFFFFFFFFu;
	}

	void SceneRenderer::SetDebugLines(const RHI::DebugLineVertex* vertices, uint32_t count)
	{
		if (IsInitialized()) _rhi->SetDebugLines(vertices, count);
	}

	void SceneRenderer::SetShadowSettings(const RHI::ShadowSettings& settings)
	{
		if (IsInitialized()) _rhi->SetShadowSettings(settings);
	}

	void SceneRenderer::SetParticleCapacity(uint32_t capacity)
	{
		if (IsInitialized()) _rhi->SetParticleCapacity(capacity);
	}

	void SceneRenderer::SetParticleEmitters(const RHI::ParticleEmitterData* emitters, uint32_t count, const RHI::ParticleFrameSettings& settings)
	{
		if (IsInitialized()) _rhi->SetParticleEmitters(emitters, count, settings);
	}

	RHI::ParticleStats SceneRenderer::GetParticleStats() const
	{
		return IsInitialized() ? _rhi->GetParticleStats() : RHI::ParticleStats();
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
		// v1.5 : 全部のカメラの行列を作って RHI へ (0 番はフレームの始めのカメラにもする)
		if (_cameras.empty()) _cameras.push_back(_camera);
		RHI::CameraView views[RHI::MAX_CAMERAS];
		const uint32_t count = static_cast<uint32_t>((std::min)(_cameras.size(), static_cast<size_t>(RHI::MAX_CAMERAS)));
		for (uint32_t c = 0; c < count; ++c)
		{
			const CameraDesc& camera = _cameras[c];
			const ::DirectX::XMVECTOR position = ::DirectX::XMVectorSet(camera.Position[0], camera.Position[1], camera.Position[2], 1.0f);
			const ::DirectX::XMVECTOR forward  = ::DirectX::XMVectorSet(camera.Forward[0],  camera.Forward[1],  camera.Forward[2],  0.0f);
			const ::DirectX::XMVECTOR up       = ::DirectX::XMVectorSet(camera.Up[0],       camera.Up[1],       camera.Up[2],       0.0f);
			const ::DirectX::XMMATRIX view     = ::DirectX::XMMatrixLookToLH(position, forward, up);

			const float nearZ  = (std::max)(camera.NearZ, 0.0001f);
			const float farZ   = (std::max)(camera.FarZ, nearZ + 0.001f);
			const float aspect = camera.Aspect > 0.0f ? camera.Aspect : width / height;   // v1.5 : カメラの範囲の比
			const ::DirectX::XMMATRIX projection = camera.Orthographic != 0
				? ::DirectX::XMMatrixOrthographicLH(height * aspect, height, nearZ, farZ)
				: ::DirectX::XMMatrixPerspectiveFovLH(camera.FieldOfViewY, aspect, nearZ, farZ);
			views[c].View        = toFloat4x4(view);
			views[c].Projection  = toFloat4x4(projection);
			views[c].CullingMask = camera.CullingMask != 0 ? camera.CullingMask : 0xFFFFFFFFu;
		}
		_rhi->SetCameraViews(views, count);
		_rhi->SetViewProjection(views[0].View, views[0].Projection);
	}
}

/************************************
* End of file
************************************/
