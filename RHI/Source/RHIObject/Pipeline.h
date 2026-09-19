
/***********************************************************************************
* @file    Pipeline.h
* @brief   RHI Piepeline
* @details 
*
* @author  Keigo Onari
* @date    2026/09/16
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_PIPELINE_H_
#define RHI_PIPELINE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <string>
#include "IRHIObject.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

class ID3D12PipelineState;
class ID3D12RootSignature;

namespace RHI
{
    namespace DirectX12
    {
        class PipelineState;
        class RootSignature;
    }

    namespace ObjectInterface
    {
        class Device;

        class Pipeline : IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/
            struct CustomPipeline
            {
                const char* PipelineName;
                std::unique_ptr<RHI::DirectX12::PipelineState> Pipeline;
            };

        public:          /*---コンストラクタ・デストラクタ---*/
            Pipeline();
            ~Pipeline();


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;
            RHI_RESULT Initialize(Device* device)override;

            ID3D12PipelineState* GetPipelineState()const;
            ID3D12RootSignature* GetRootSignature()const;

            RHI_RESULT MakeCustomPipeline(
                const std::wstring& vsPath,
                const std::string& vsEntry,
                const std::wstring& psPath,
                const std::string& psEntry
            );

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            std::unique_ptr<DirectX12::PipelineState> _pipelineState;
            std::unique_ptr<DirectX12::RootSignature> _rootSignature;
            std::vector<CustomPipeline>               _customPipeline;

            RHI_RESULT makePipeline(Device* device);
        };

    }
}

#endif // RHI_PIPELINE_H_

/************************************
* End of file
************************************/
