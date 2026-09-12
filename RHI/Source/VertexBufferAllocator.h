
/***********************************************************************************
* @file    dx12_vertexBufferAllocator.h
* @brief   DX12のテクスチャ括管理
* @details
*
* @author  Keigo Onari
* @date    2026/09/12
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef VERTEX_BUFFER_ALLOCATOR_H_
#define VERTEX_BUFFER_ALLOCATOR_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <windows.h>


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
    struct VertexBufferAllocation
    {
        UINT StartIndexLocation;
        INT  BaseVertexLocation;
    };

    class VertexBufferAllocator
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    public:          /*---コンストラクタ・デストラクタ---*/
        VertexBufferAllocator() {}
        ~VertexBufferAllocator() {}

    public:          /*---クラスメソッド---*/



        /***********************************************************************************
         * @brief メモリ情報の作成
         * @details
         *
         * @param[in] IDXGIFactory6* pDxgiFactory DXGIFactory6のポインタ
         * @return VertexBufferAllocation 頂点バッファーのメモリ情報
         ************************************************************************************/
        VertexBufferAllocation Allocate();

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/


    };
}

#endif // VERTEX_BUFFER_ALLOCATOR_H_

/************************************
* End of file
************************************/
