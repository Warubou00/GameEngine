
/***********************************************************************************
* @file    ICommandList.h
* @brief   API interface commandList
* @details
*
* @author  Keigo Onari
* @date    2026/09/20
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/
#pragma once
#ifndef API_INTERFACE_COMMAND_LIST_H_
#define API_INTERFACE_COMMAND_LIST_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IAPI.h"

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
	namespace APIInterface
	{
		class IDevice;
		class IPipeline;
		class IDescriptorHeap;
		class IResourceBinding;
		class IResource;
		class ISwapChain;

		class ICommandList
		{
		public:
			enum class ResourceState
			{// DX12を採用(idxを合わせるため)
				RESOURCE_STATE_COMMON = 0,
				RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER = 0x1,
				RESOURCE_STATE_INDEX_BUFFER = 0x2,
				RESOURCE_STATE_RENDER_TARGET = 0x4,
				RESOURCE_STATE_UNORDERED_ACCESS = 0x8,
				RESOURCE_STATE_DEPTH_WRITE = 0x10,
				RESOURCE_STATE_DEPTH_READ = 0x20,
				RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE = 0x40,
				RESOURCE_STATE_PIXEL_SHADER_RESOURCE = 0x80,
				RESOURCE_STATE_STREAM_OUT = 0x100,
				RESOURCE_STATE_INDIRECT_ARGUMENT = 0x200,
				RESOURCE_STATE_COPY_DEST = 0x400,
				RESOURCE_STATE_COPY_SOURCE = 0x800,
				RESOURCE_STATE_RESOLVE_DEST = 0x1000,
				RESOURCE_STATE_RESOLVE_SOURCE = 0x2000,
				RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE = 0x400000,
				RESOURCE_STATE_SHADING_RATE_SOURCE = 0x1000000,
				RESOURCE_STATE_RESERVED_INTERNAL_8000 = 0x8000,
				RESOURCE_STATE_RESERVED_INTERNAL_4000 = 0x4000,
				RESOURCE_STATE_RESERVED_INTERNAL_100000 = 0x100000,
				RESOURCE_STATE_RESERVED_INTERNAL_40000000 = 0x40000000,
				RESOURCE_STATE_RESERVED_INTERNAL_80000000 = 0x80000000,
				RESOURCE_STATE_GENERIC_READ = (((((0x1 | 0x2) | 0x40) | 0x80) | 0x200) | 0x800),
				RESOURCE_STATE_ALL_SHADER_RESOURCE = (0x40 | 0x80),
				RESOURCE_STATE_PRESENT = 0,
				RESOURCE_STATE_PREDICATION = 0x200,
				RESOURCE_STATE_VIDEO_DECODE_READ = 0x10000,
				RESOURCE_STATE_VIDEO_DECODE_WRITE = 0x20000,
				RESOURCE_STATE_VIDEO_PROCESS_READ = 0x40000,
				RESOURCE_STATE_VIDEO_PROCESS_WRITE = 0x80000,
				RESOURCE_STATE_VIDEO_ENCODE_READ = 0x200000,
				RESOURCE_STATE_VIDEO_ENCODE_WRITE = 0x800000,
			};

		public:
			ICommandList() {}
			virtual ~ICommandList() = default;

			/***********************************************************************************
			 * @brief 初期化処理
			 * @details core explanation
			 *
			 * @param[in] RHI::APIInterface::IDeice* iDevice デバイス
			 * @return 無し
			 ************************************************************************************/
			virtual RHI_API_RESULT Initialize(IDevice* iDevice) = 0;

			/***********************************************************************************
			 * @brief レンダリング初期処理
			 * @details core explanation
			 *
			 * @param[in] IPepeline パイプライン
			 * @return 無し
			 ************************************************************************************/
			virtual void Begin(IPipeline* iPipeline) = 0;

			virtual void ResourceBarriar(IResource* iResource, ResourceState before, ResourceState after) = 0;

			/***********************************************************************************
			 * @brief レンダリング
			 * @details core explanation
			 *
			 * @param[in] IPipeline* パイプライン
			 * @param[in] IDescriptorHeap* ディスクリプタ
			 * @param[in] IResourceBinding* リソースバインディング
			 * @return 無し
			 ************************************************************************************/
			virtual void Render(
				IPipeline* iPipeline,
				IDescriptorHeap* iDescHeap,
				IResourceBinding* iResourceBinding,
				IResource* iResource,
				ISwapChain* iSwapChain) = 0;

			/***********************************************************************************
			 * @brief レンダリング修了処理
			 * @details core explanation
			 *
			 * @param[in]
			 * @return 無し
			 ************************************************************************************/
			virtual void End() = 0;

			/***********************************************************************************
			 * @brief 解放関数
			 * @details ComPtrは解放しない
			 *
			 ************************************************************************************/
			virtual void Release(void) = 0;

		private:
		};
	}
}

#endif // API_INTERFACE_COMMAND_LIST_H_

/************************************
* End of file
************************************/