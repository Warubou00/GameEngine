
/***********************************************************************************
* @file    rhiExport.h
* @brief   RHI.dll の export / import 切り替え
* @details RHI プロジェクトでは RHI_EXPORTS を定義してビルドする (dllexport)。
*          RHI を使う側 (Renderer など) では定義しないので dllimport になる。
*          (以前は常に dllexport で、使う側から見ても export 扱いになっていた : 3-8)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_PUBLIC_EXPORT_H_
#define RHI_PUBLIC_EXPORT_H_

#ifdef RHI_EXPORTS
#define RHI_API __declspec(dllexport)
#else
#define RHI_API __declspec(dllimport)
#endif

#endif // RHI_PUBLIC_EXPORT_H_
