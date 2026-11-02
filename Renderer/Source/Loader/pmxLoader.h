
/***********************************************************************************
* @file    pmxLoader.h
* @brief   PMX (MikuMikuDance) モデルのローダー
* @details 描画に使う所 (頂点 / 面 / テクスチャ / 材質) だけを読み、Mesh にして返す。
*          ボーン / モーフ / 剛体 / ジョイントは読まない (アニメーションを入れる時に追加する)
*          PMX 2.0 / 2.1、文字コード UTF-16 / UTF-8 の両方に対応
*
*          DirectX11 時代のローダー (namespace ko / MeshRenderer に直接書き込む) を、
*          今の Renderer::IModelLoader (Mesh を返す) に合わせて作り直した
*
* @author  Keigo Onari
* @date    2024/4/1
* @version v_2.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RENDERER_PMX_LOADER_H_
#define RENDERER_PMX_LOADER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>
#include <fstream>
#include <string>
#include "IModelLoader.h"
#include "../common.h"

namespace Renderer
{
    class RENDERER_API_CLASS PmxLoader : public IModelLoader
    {
    public:
        PmxLoader() = default;
        ~PmxLoader()override = default;

        /***********************************************************************************
         * @brief PMX を読み込む
         * @return Mesh* 読み込んだメッシュ (呼んだ側が Mesh と Materials を delete する)。失敗時 nullptr
         ************************************************************************************/
        Mesh* LoadModel(const std::wstring& fileName)override;

    private:
        // 文字列 (長さ 4byte + 本体)。文字コードはヘッダーの指定 (UTF-16 / UTF-8)
        bool readText(std::ifstream& ifs, std::wstring& output);
        bool skipText(std::ifstream& ifs);

        // 番号 (1 / 2 / 4 byte)。頂点の番号は符号なし、それ以外 (テクスチャ・ボーン等) は符号あり (-1 = 無し)
        int32_t readIndex(std::ifstream& ifs, uint8_t size, bool isUnsigned);

        // ファイルのあるフォルダ (末尾に区切り付き)。テクスチャのパスはここからの相対
        static std::wstring folderOf(const std::wstring& filePath);

        bool _utf8 = false;   // 文字コード (false = UTF-16LE)
    };
}

#endif // RENDERER_PMX_LOADER_H_

/************************************
* End of file
************************************/
