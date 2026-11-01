
/***********************************************************************************
* @file    logger.cpp
* @brief   ÉçÉKÅ[
* @details 
*
* @author  Keigo Onari
* @date    2026/09/16
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    ÇªÇÃëº
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "logger.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Core
{
	LogCallback Logger::_logCallback    = nullptr;
	WLogCallback Logger::_w_logCallback = nullptr;
	bool Logger::_initialized           = false;

	void Logger::Initialize(LogCallback logCallback, WLogCallback w_logCallback)
	{
		_logCallback   = logCallback;
		_w_logCallback = w_logCallback;

		_initialized   = true;
	}

	void Logger::Log(const char* message)
	{
		if (_initialized)
			_logCallback(message);
	}

	void Logger::Log(const wchar_t* message)
	{
		if (_initialized)
			_w_logCallback(message);
	}
}

/************************************
* End of file
************************************/
