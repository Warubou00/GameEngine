
/***********************************************************************************
* @file    pmxLoader.cpp
* @brief   PMX (MikuMikuDance) モデルのローダー
* @details 形式 : PMX 2.0 / 2.1 (https://gist.github.com/felixjones/f8a06bd48f9da9a4539f の仕様に沿う)
*
*          DirectX11 時代のローダーからの主な修正
*            - Mesh を返す形にした (以前は MeshRenderer / ModelData に直接書き込んでいた)
*            - 追加 UV を 2 回読んでいて、追加 UV があるモデルでは以降の位置がずれていた
*            - 1 / 2 byte の符号付きの番号 (テクスチャ等) を符号拡張していなかった
*              (-1 = 「無し」が 255 / 65535 になり、配列の外を読んでいた)
*            - 文字列を 512 文字の固定の配列に読んでいて、長い文字列で配列の外に書いていた
*            - UTF-8 のモデルを読めなかった / PMX 2.1 を読めなかった
*            - ボーンは描画に使わないので読まない (アニメーションを入れる時に追加する)
*
* @author  Keigo Onari
* @date    2024/4/1
* @version v_2.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "pmxLoader.h"

#include <memory>
#include <vector>
#include <windows.h>

#include "../mesh.h"
#include "../material.h"
#include "../../../Core/Source/Log/logger.h"

namespace Renderer
{
    namespace
    {
        // ヘッダーの「その他の情報」の並び
        enum GlobalIndex
        {
            TEXT_ENCODING = 0,     // 0 : UTF-16LE / 1 : UTF-8
            ADDITIONAL_UV,         // 追加 UV の数 (0〜4)
            VERTEX_INDEX_SIZE,     // 1 / 2 / 4
            TEXTURE_INDEX_SIZE,
            MATERIAL_INDEX_SIZE,
            BONE_INDEX_SIZE,
            MORPH_INDEX_SIZE,
            RIGID_BODY_INDEX_SIZE,
            GLOBAL_COUNT
        };

        // 材質のフラグ
        constexpr uint8_t MATERIAL_FLAG_DOUBLE_SIDED = 0x01;

        // 頂点の位置などは、ファイルから直接 Core::Vector に読む (並びが float × n であること)
        static_assert(sizeof(Core::Vector2) == 8,  "Core::Vector2 must be float x2");
        static_assert(sizeof(Core::Vector3) == 12, "Core::Vector3 must be float x3");

        template <typename T>
        bool readValue(std::ifstream& ifs, T& value)
        {
            ifs.read(reinterpret_cast<char*>(&value), sizeof(T));
            return static_cast<bool>(ifs);
        }

        void skipBytes(std::ifstream& ifs, std::streamoff bytes)
        {
            ifs.seekg(bytes, std::ios::cur);
        }

        void logFailed(const wchar_t* what, const std::wstring& fileName)
        {
            Core::Logger::Log((std::wstring(L"[PmxLoader] ") + what + L" : " + fileName).c_str());
        }
    }

    Mesh* PmxLoader::LoadModel(const std::wstring& fileName)
    {
        std::ifstream ifs(fileName, std::ios::binary);
        if (!ifs)
        {
            logFailed(L"Cannot open", fileName);
            return nullptr;
        }

        // ---------- ヘッダー ----------
        char signature[4] = {};
        ifs.read(signature, 4);
        if (signature[0] != 'P' || signature[1] != 'M' || signature[2] != 'X' || signature[3] != ' ')
        {
            logFailed(L"Not a PMX file", fileName);
            return nullptr;
        }

        float version = 0.0f;
        readValue(ifs, version);
        if (version < 2.0f || version > 2.1f + 1e-4f)   // 以前は 2.0 だけだった
        {
            logFailed(L"Unsupported PMX version", fileName);
            return nullptr;
        }

        uint8_t globalCount = 0;
        readValue(ifs, globalCount);
        if (globalCount < GLOBAL_COUNT)
        {
            logFailed(L"Broken header", fileName);
            return nullptr;
        }
        uint8_t globals[256] = {};
        ifs.read(reinterpret_cast<char*>(globals), globalCount);
        _utf8 = globals[TEXT_ENCODING] == 1;   // 以前は UTF-8 のモデルを読めなかった

        const uint8_t additionalUv     = globals[ADDITIONAL_UV];
        const uint8_t vertexIndexSize  = globals[VERTEX_INDEX_SIZE];
        const uint8_t textureIndexSize = globals[TEXTURE_INDEX_SIZE];
        const uint8_t boneIndexSize    = globals[BONE_INDEX_SIZE];

        // ---------- モデルの情報 (名前 / 英名 / コメント / 英コメント) は使わない ----------
        for (int i = 0; i < 4; ++i) skipText(ifs);

        auto mesh = std::make_unique<Mesh>();

        // ---------- 頂点 ----------
        int32_t vertexCount = 0;
        readValue(ifs, vertexCount);
        if (vertexCount <= 0 || !ifs)
        {
            logFailed(L"No vertices", fileName);
            return nullptr;
        }
        mesh->Vertices.resize(static_cast<size_t>(vertexCount));
        for (auto& vertex : mesh->Vertices)
        {
            // MMD は DirectX と同じ左手系なので、そのまま使える
            readValue(ifs, vertex.Position);
            readValue(ifs, vertex.Normal);
            readValue(ifs, vertex.UV);
            vertex.Color = Core::Vector4(1.0f);   // 色は材質で付ける

            // 追加 UV (float4 × n) は使わない (以前は 2 回読んでいて位置がずれていた)
            skipBytes(ifs, static_cast<std::streamoff>(additionalUv) * 16);

            // ウェイト (アニメーションを入れる時に使う。今は読み飛ばす)
            uint8_t deform = 0;
            readValue(ifs, deform);
            switch (deform)
            {
            case 0: skipBytes(ifs, boneIndexSize);                       break;   // BDEF1 : ボーン 1
            case 1: skipBytes(ifs, boneIndexSize * 2 + 4);               break;   // BDEF2 : ボーン 2 + ウェイト 1
            case 2: skipBytes(ifs, boneIndexSize * 4 + 16);              break;   // BDEF4 : ボーン 4 + ウェイト 4
            case 3: skipBytes(ifs, boneIndexSize * 2 + 4 + 12 * 3);      break;   // SDEF  : BDEF2 + C / R0 / R1
            case 4: skipBytes(ifs, boneIndexSize * 4 + 16);              break;   // QDEF  : (2.1) BDEF4 と同じ並び
            default:
                logFailed(L"Unknown weight type", fileName);
                return nullptr;
            }
            skipBytes(ifs, 4);   // エッジの倍率
        }

        // ---------- 面 (インデックス。3 個で 1 面) ----------
        int32_t indexCount = 0;
        readValue(ifs, indexCount);
        std::vector<uint32_t> indices(static_cast<size_t>((std::max)(indexCount, 0)));
        for (auto& index : indices)
        {
            index = static_cast<uint32_t>(readIndex(ifs, vertexIndexSize, true));
            if (index >= mesh->Vertices.size())
            {
                logFailed(L"Vertex index out of range", fileName);
                return nullptr;
            }
        }

        // ---------- テクスチャ (モデルのフォルダからの相対パス) ----------
        const std::wstring folder = folderOf(fileName);
        int32_t textureCount = 0;
        readValue(ifs, textureCount);
        std::vector<std::wstring> textures(static_cast<size_t>((std::max)(textureCount, 0)));
        for (auto& texture : textures)
        {
            std::wstring path;
            readText(ifs, path);
            texture = folder + path;
        }

        // ---------- 材質 (材質ごとに、面を前から順に使う) ----------
        int32_t materialCount = 0;
        readValue(ifs, materialCount);
        size_t indexCursor = 0;
        for (int32_t m = 0; m < materialCount && ifs; ++m)
        {
            auto material = new Material();
            readText(ifs, material->Name);   // 名前 (v1.5 : マテリアルのアセットの名前に使う。以前は読み飛ばした)
            skipText(ifs);   // 英名

            float diffuse[4] = {}, specular[3] = {}, specularity = 0.0f, ambient[3] = {};
            readValue(ifs, diffuse);
            readValue(ifs, specular);
            readValue(ifs, specularity);
            readValue(ifs, ambient);
            material->Diffuse  = Core::Vector4(diffuse[0], diffuse[1], diffuse[2], diffuse[3]);
            material->Specular = Core::Vector4(specular[0], specular[1], specular[2], specularity);
            material->Ambient  = Core::Vector4(ambient[0], ambient[1], ambient[2], 1.0f);

            uint8_t flags = 0;
            readValue(ifs, flags);
            material->DoubleSided = (flags & MATERIAL_FLAG_DOUBLE_SIDED) != 0;
            skipBytes(ifs, 16 + 4);   // エッジの色 / 大きさ

            const int32_t textureIndex = readIndex(ifs, textureIndexSize, false);
            if (textureIndex >= 0 && textureIndex < static_cast<int32_t>(textures.size()))
            {
                material->TexturePath = textures[textureIndex];
                material->UseTexture  = true;
            }
            readIndex(ifs, textureIndexSize, false);   // スフィアテクスチャ (今は使わない)
            skipBytes(ifs, 1);                          // スフィアのモード

            uint8_t sharedToon = 0;
            readValue(ifs, sharedToon);
            if (sharedToon != 0) skipBytes(ifs, 1);                     // 共有トゥーン (toon01〜10)
            else                 readIndex(ifs, textureIndexSize, false); // 個別トゥーンのテクスチャ
            skipText(ifs);   // メモ

            int32_t materialIndexCount = 0;
            readValue(ifs, materialIndexCount);
            if (materialIndexCount < 0 || indexCursor + static_cast<size_t>(materialIndexCount) > indices.size())
            {
                delete material;
                logFailed(L"Material face count out of range", fileName);
                break;
            }

            // MMD は材質の不透明度とテクスチャのアルファで半透明になるので、全部「半透明として描く」
            // (MMD と同じく、ファイルの材質の順番に、深度も書きながら描く)
            material->Transparent = true;

            mesh->Indecies.emplace_back(indices.begin() + indexCursor, indices.begin() + indexCursor + materialIndexCount);
            mesh->Materials.push_back(material);
            indexCursor += static_cast<size_t>(materialIndexCount);
        }
        // ボーン / モーフ / 表示枠 / 剛体 / ジョイントは読まない

        if (mesh->Indecies.empty())
        {
            logFailed(L"No materials", fileName);
            for (auto* material : mesh->Materials) delete material;
            return nullptr;
        }
        return mesh.release();
    }

    bool PmxLoader::readText(std::ifstream& ifs, std::wstring& output)
    {
        output.clear();
        int32_t bytes = 0;
        if (!readValue(ifs, bytes) || bytes < 0) return false;
        if (bytes == 0) return true;

        // 以前は 512 文字の固定の配列に読んでいて、長い文字列で配列の外に書いていた
        std::vector<char> buffer(static_cast<size_t>(bytes));
        ifs.read(buffer.data(), bytes);
        if (!ifs) return false;

        if (_utf8)
        {
            const int length = MultiByteToWideChar(CP_UTF8, 0, buffer.data(), bytes, nullptr, 0);
            output.resize(static_cast<size_t>(length));
            MultiByteToWideChar(CP_UTF8, 0, buffer.data(), bytes, output.data(), length);
        }
        else
        {
            output.assign(reinterpret_cast<const wchar_t*>(buffer.data()), static_cast<size_t>(bytes) / sizeof(wchar_t));
        }
        return true;
    }

    bool PmxLoader::skipText(std::ifstream& ifs)
    {
        int32_t bytes = 0;
        if (!readValue(ifs, bytes) || bytes < 0) return false;
        skipBytes(ifs, bytes);
        return static_cast<bool>(ifs);
    }

    int32_t PmxLoader::readIndex(std::ifstream& ifs, uint8_t size, bool isUnsigned)
    {
        switch (size)
        {
        case 1:
        {
            uint8_t value = 0;
            readValue(ifs, value);
            return isUnsigned ? static_cast<int32_t>(value) : static_cast<int32_t>(static_cast<int8_t>(value));
        }
        case 2:
        {
            uint16_t value = 0;
            readValue(ifs, value);
            return isUnsigned ? static_cast<int32_t>(value) : static_cast<int32_t>(static_cast<int16_t>(value));
        }
        default:
        {
            int32_t value = 0;
            readValue(ifs, value);
            return value;
        }
        }
    }

    std::wstring PmxLoader::folderOf(const std::wstring& filePath)
    {
        const size_t slash = filePath.find_last_of(L"/\\");
        return slash != std::wstring::npos ? filePath.substr(0, slash + 1) : std::wstring();
    }
}

/************************************
* End of file
************************************/
