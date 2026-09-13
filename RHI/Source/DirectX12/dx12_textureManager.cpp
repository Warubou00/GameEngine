
/***********************************************************************************
* @file    dx12_textureManager.cpp
* @brief   DX12のテクスチャ括管理
* @details 
*
* @author  Keigo Onari
* @date    2026/04/24
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
#include "dx12_textureManager.h"
#include <DirectXTex.h>

#pragma comment(lib, "DirectXTex.lib")

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
	namespace DirectX12
	{
        HRESULT TextureManager::Create(ID3D12Device* device)
        {
			return S_OK;
        }

		TextureHandle TextureManager::ReadTexture(const std::wstring& fileName)
		{
			HRESULT result;
			//WICテクスチャのロード
			DirectX::TexMetadata metadata = {};
			DirectX::ScratchImage scratchImg = {};
			result = LoadFromWICFile(fileName.c_str(), DirectX::WIC_FLAGS_NONE, &metadata, scratchImg);
			auto img = scratchImg.GetImage(0, 0, 0);//生データ抽出

			return TextureHandle(0);
		}
	}
}

/************************************
* End of file
************************************/
