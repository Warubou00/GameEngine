
/***********************************************************************************
* @file    dx12_pipelineStateManager.h
* @brief   PSO管理クラス
* @details 
*
* @author  Keigo Onari
* @date    2026/02/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef DX12_PIPELINE_MANAGER_H_
#define DX12_PIPELINE_MANAGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"
#include <vector>
#include <string>
#include "../../../Core/Source/Math/hash.h"

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
        class PipelineStateManager
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        private:          /*---コンストラクタ・デストラクタ---*/
            PipelineStateManager();
            ~PipelineStateManager();


        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


            /***********************************************************************************
             * @brief インスタンスの取得
             * @details シングルトン設計
             *
             * @return GraphicsPipelineManager* グラフィックスパイプラインのマネージャーのポインタ
             ************************************************************************************/
            static PipelineStateManager* getInstance();



            /***********************************************************************************
             * @brief PSOの登録
             * @details PSOの登録
             *
             * @param[in] ID3D12PipelineState* pipelineState PSOポインタ
             * @return int 登録ID
             ************************************************************************************/
            int Register(ID3D12PipelineState* pipelineState, const std::string& pipelineName);


            /***********************************************************************************
             * @brief Hashのリリース
             *
             ************************************************************************************/
            void Release(void);

        private :         /*---メンバ変数---*/

            static PipelineStateManager* Instance;

            std::vector<Microsoft::WRL::ComPtr<ID3D12PipelineState>> _states;

            Core::Hash* _hash = nullptr;
            const size_t PIPE_STATE_MAX_NUM = 100;

            void initialize(void);
        };

    }
}


#endif // DX12_PIPELINE_MANAGER_H_

/************************************
* End of file
************************************/
