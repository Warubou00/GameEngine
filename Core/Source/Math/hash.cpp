
/***********************************************************************************
* @file    hash.cpp
* @brief   ハッシュ化するクラス
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
#include "hash.h"

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
	Hash::Hash() {}
	Hash::~Hash() {}

	bool Hash::Initialize(const size_t hashMax)
	{
		_hashMax = hashMax + 1;

		return true;
	}

	int Hash::HashIntegerForName(const char* name, const size_t nameSize)
	{
		setSeed(name, nameSize);

		uint32_t value = Random::Xorshift();
		return (int)value % _hashMax;
	}

	uint32_t Hash::HashUIntegerForName(const char* name, const size_t nameSize)
	{
		setSeed(name, nameSize);

		uint32_t value = Random::Xorshift();
		return value % _hashMax;
	}

	int Hash::HashIntegerForInteger(int id)
	{
		setSeed(id);

		uint32_t value = Random::Xorshift();
		return (int)value % _hashMax;
	}

	uint32_t Hash::HashUIntegerForInteger(int id)
	{
		setSeed(id);

		uint32_t value = Random::Xorshift();
		return value % _hashMax;
	}

	void Hash::setSeed(const char* name, const size_t nameSize)
	{
		uint32_t seed = 0u;

		for (int i = 0; i < nameSize; i++)
		{
			seed += (uint32_t)name[i];
		}

		Random::SetSeed(seed);
	}

	void Hash::setSeed(int id)
	{
		Random::SetSeed((uint32_t)id);
	}
}

/************************************
* End of file
************************************/
