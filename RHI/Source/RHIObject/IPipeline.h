
/***********************************************************************************
* @file    IPipeline.h
* @brief   RHI interface pipeline
* @details 
*
* @author  Keigo Onari
* @date    2026/09/21
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_OBJECT_INTERFACE_PIPELINE_H_
#define RHI_OBJECT_INTERFACE_PIPELINE_H_

/******************************************************************
* Include Headers
******************************************************************/
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



namespace RHI
{
    namespace ObjectInterface
    {
        class IDevice;

        class IPipeline : public IRHIObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            IPipeline() {};
            virtual ~IPipeline() = default;


        public:          /*---クラスメソッド---*/

            virtual RHI_RESULT Initialize(RHI::ObjectInterface::IDevice* device) = 0;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_PIPELINE_H_

/************************************
* End of file
************************************/
