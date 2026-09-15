
/***********************************************************************************
* @file    dx12_pipelineStateManager.cpp
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


/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_pipelineStateManager.h"
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
		PipelineStateManager::PipelineStateManager() {}
		PipelineStateManager::~PipelineStateManager() {}

		int PipelineStateManager::Register(ID3D12PipelineState* state, const std::string& pipelineName)
		{
			int hashValue = _hash->HashIntegerForName(pipelineName.c_str(), pipelineName.size());

			// hashValueに当てはめる
			if (_states[hashValue])
			{
				// nullptrでない場合、次へ進める
				for (int i = hashValue + 1; i < PIPE_STATE_MAX_NUM ; i++)
				{
					// nulllptrなら入れる
					// TODO : 最後まで走査した場合の0からの走査はまた作る
					if (!_states[i])
					{
						_states[i] = state;
						hashValue = i;
						break;
					}
				}
			}
			else
			{
				_states[hashValue] = state;
			}

			return hashValue;
		}

		void PipelineStateManager::Release(void)
		{
			if (_hash)
			{
				delete _hash;
				_hash = nullptr;
			}
		}
		

		void PipelineStateManager::initialize(void)
		{
			// パイプライン格納用のバッファを用意
			_states.resize(PIPE_STATE_MAX_NUM);

			_hash = new Core::Hash();
			_hash->Initialize(PIPE_STATE_MAX_NUM - 1);
		}
	}
}

/************************************
* End of file
************************************/
