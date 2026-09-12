
/***********************************************************************************
 * @file    float4.h
 * @brief   独自のFloat4
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
#ifndef FLOAT4_H_
#define FLOAT4_H_

namespace Core
{
	// ==========================================================
	// Vector4
	// ========================================================== 
	struct Float4
	{
		union {
			float _float[4];
			struct {
				float x, y, z, w;
			};
		};

		// コンストラクタ
		// ==========================================================
		Float4() = default;
		Float4(float X, float Y, float Z, float W) :x(X), y(Y), z(Z), w(W) {}
		Float4(float XYZW) :x(XYZW), y(XYZW), z(XYZW), w(XYZW) {}
	};
}

#endif // FLOAT4_H_

/************************************
* End of file
************************************/