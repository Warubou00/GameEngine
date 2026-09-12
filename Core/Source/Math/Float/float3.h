
/***********************************************************************************
 * @file    float3.h
 * @brief   独自のFloat3
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
#ifndef FLOAT3_H_
#define FLOAT3_H_

namespace Core
{
	// =============================================================
	// Float3
	// =============================================================
	struct Float3
	{
		union {
			float _float[3];
			struct {
				float x, y, z;
			};
		};

		// =============================================================
		// コンストラクタ
		// =============================================================
		Float3() = default;
		Float3(float X, float Y, float Z) :x(X), y(Y), z(Z) {}
		Float3(float XYZ) :x(XYZ), y(XYZ), z(XYZ) {}
	};
}

#endif // FLOAT3_H_

/************************************
* End of file
************************************/