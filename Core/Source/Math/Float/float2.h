
/***********************************************************************************
 * @file    float2.h
 * @brief   独自のFloat2
 * @details 
 *
 * @author  Keigo Onari
 * @date    2026/1/13
 * @version v_1.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note    
 ************************************************************************************/

#pragma once
#ifndef FLOAT2_H_
#define FLOAT2_H_

namespace Core
{
	// =============================================================
	// Float2
	// ============================================================= 
	struct Float2
	{
		union {
			float _float[2];
			struct {
				float x, y;
			};
		};

		// =============================================================
		// コンストラクタ
		// =============================================================
		Float2() = default;
		Float2(float X, float Y) :x(X), y(Y) {}
		Float2(float XY) : x(XY), y(XY) {}

	};
}

#endif // FLOAT2_H_

/************************************
* End of file
************************************/