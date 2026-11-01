
/***********************************************************************************
* @file    TextureHandle.h
* @brief   texture handle
* @details 
*
* @author  Keigo Onari
* @date    2026/09/22
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_OBJECT_TEXTURE_HANDLE_H_
#define RHI_OBJECT_TEXTURE_HANDLE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <stdint.h>
#include "IHandle.h"


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
        struct TextureHandle : public IHandle
        {
            // 既定テクスチャ(白 1x1)の番号。読み込みに失敗した時もこれが返る
            static constexpr uint32_t DEFAULT_TEXTURE_ID = 0;

            // シェーダーのテクスチャ配列 g_Textures[] の添え字 (ディスクリプタヒープの番号ではない)
            uint32_t TextureID = DEFAULT_TEXTURE_ID;
        };
    }
}


#endif // RHI_OBJECT_TEXTURE_HANDLE_H_

/************************************
* End of file
************************************/
