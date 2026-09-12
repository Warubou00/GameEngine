/***********************************************************************************
* @file    soundLoaderBase.h
* @brief   サウンドローダーの親クラス
* @details
*
* @author  Keigo Onari
* @date    2025/09/05
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/
#pragma once
#ifndef _SOUND_LOADER_H_
#define _SOUND_LOADER_H_

#include <string>

namespace Library
{
	namespace Sound
	{
		namespace Loader
		{
			/// <summary>
			/// サウンドのローダー親クラス
			/// </summary>
			class SoundLoaderBase
			{
			public:
				virtual bool Load(const std::string& filePath) { return true; };
			};
		}
	}
}

#endif // _SOUND_LOADER_H_

// ==============================
// end of file
// ==============================