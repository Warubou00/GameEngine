
/***********************************************************************************
* @file    modelData.h
* @brief   モデルデータを表す構造体
* @details 
*
* @author  Keigo Onari
* @date    2024/4/1
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef MODEL_DATA_H_
#define MODEL_DATA_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <string>
#include "../../../Core/Source/Math/Vector/vector.h"

#include "../common.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Renderer
{

	struct RENDERER_API_CLASS ModelData
	{
		static constexpr int NO_DATA_FLAG = -1;

		/******************************************************************
		* Internal Enumeration Declarations
		******************************************************************/


		/******************************************************************
		* Internal Structure Declarations
		******************************************************************/
		//struct ModelVertex
		//{
		//	std::vector<ko::Vector2> additionalUV;

		//	// ボーンウェイト
		//	struct Weight
		//	{
		//		enum Type
		//		{
		//			BDEF1,
		//			BDEF2,
		//			BDEF4,
		//			SDEF,
		//		};

		//		Type type;
		//		int born1;
		//		int	born2;
		//		int	born3;
		//		int	born4;
		//		float weight1;
		//		float weight2;
		//		float weight3;
		//		float weight4;
		//		ko::Vector3 c;
		//		ko::Vector3 r0;
		//		ko::Vector3 r1;
		//	} weight;

		//	// エッジ倍率
		//	float edgeMagnif;
		//};

		//struct Bone
		//{
		//	// ボーン名
		//	std::wstring name;
		//	// English version
		//	std::string nameEnglish;
		//	ko::Vector3 position;
		//	int parentIndex;
		//	int transformationLevel;
		//	unsigned short flag;
		//	ko::Vector3 coordOffset;
		//	int childrenIndex;
		//	int impartParentIndex;
		//	float impartRate;
		//	// 固定軸方向ベクトル
		//	ko::Vector3 fixedAxis;
		//	// ローカルのX軸方向ベクトル
		//	ko::Vector3 localAxisX;
		//	// ローカルのZ軸方向ベクトル
		//	ko::Vector3 localAxisZ;
		//	int externalParentKey;
		//	int ikTargetIndex;
		//	int ikLoopCount;
		//	float ikUnitAngle;
		//	struct IKLink
		//	{
		//		int index;
		//		bool existAngleLimited;
		//		ko::Vector3 limitAngleMin;
		//		ko::Vector3 limitAngleMax;
		//	};
		//	std::vector<IKLink> ikLinks;
		//};

		//std::vector<ModelVertex> vertices;
		//std::vector<Bone> bones;



	public:          /*---コンストラクタ---*/
		ModelData() {}
		~ModelData() {}


	public:          /*---クラスメソッド---*/
		/***********************************************************************************
		 * @brief 解放
		 * @details 解放
		 *
		 * @return 無し
		 ************************************************************************************/
		void Release()
		{
			//vertices.clear();
			//bones.clear();
		}

	public:          /*---アクセッサー関数群---*/


	private:         /*---メンバ変数---*/


	};

}


#endif // MODEL_DATA_H_

/************************************
* End of file
************************************/