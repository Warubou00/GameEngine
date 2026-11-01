/************************************************
* file.cpp
* ファイルストリームを扱うクラス
*
* 最終更新日:2026/09/22
* 作成者：Keigo Onari
* 最終更新 : 名前空間、返値を変更
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "file.h"

namespace Core
{
	// =============================================================
	// ファイルオープン
	// =============================================================
	CORE_RESULT File::OpenFile(const char* fName, OPEN_MODE oMode) {

		switch (oMode) {
		case _READ_ONRY:
			// ファイル開く
			_inputFile.open(fName);
			_fileMode = _READ_ONRY;

			// 開くことに成功したらKO_OKを返す
			if (_inputFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _WRITE_ONRY:
			// ファイル開く
			_outFile.open(fName);
			_fileMode = _WRITE_ONRY;

			// 開くことに成功したらKO_OKを返す
			if (_outFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _READ_ONRY_BINARY:
			// ファイル開く
			_inputFile.open(fName, std::ios::in | std::ios::binary);
			_fileMode = _READ_ONRY_BINARY;

			// 開くことに成功したらKO_OKを返す
			if (_inputFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _WRITE_ONRY_BINARY:
			// ファイル開く
			_outFile.open(fName,std::ios::out | std::ios::binary);
			_fileMode = _WRITE_ONRY_BINARY;

			// 開くことに成功したらKO_OKを返す
			if (_outFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _NON:
			_fileMode = _NON;
			break;
		}

		return false;
	}

	// =============================================================
	// ファイルオープン
	// =============================================================
	CORE_RESULT File::OpenFile(const wchar_t* fName, OPEN_MODE oMode) {

		switch (oMode) {
		case _READ_ONRY:
			// ファイル開くよ～
			_inputFile.open(fName);
			_fileMode = _READ_ONRY;

			// 開くことに成功したらKO_OKを返す
			if (_inputFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _WRITE_ONRY:
			// ファイル開くよ～
			_outFile.open(fName);
			_fileMode = _WRITE_ONRY;

			// 開くことに成功したらKO_OKを返す
			if (_outFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _READ_ONRY_BINARY:
			// ファイル開くよ～
			_inputFile.open(fName, std::ios::binary);
			_fileMode = _READ_ONRY_BINARY;

			// 開くことに成功したらKO_OKを返す
			if (_inputFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _WRITE_ONRY_BINARY:
			// ファイル開くよ～
			_outFile.open(fName, std::ios::binary);
			_fileMode = _WRITE_ONRY_BINARY;

			// 開くことに成功したらKO_OKを返す
			if (_outFile.is_open())
				return CORE_SUCCEEDED;
			break;
		case _NON:
			_fileMode = _NON;
			break;
		}

		return CORE_FAILED;
	}

	// =============================================================
	// ファイルクローズ
	// =============================================================
	void File::CloseFile(void) {
		switch (_fileMode) {
		case _READ_ONRY:
		case _READ_ONRY_BINARY:

			// 開いていたら閉じるよ～
			if (_inputFile.is_open())
				_inputFile.close();
			break;
		case _WRITE_ONRY:
		case _WRITE_ONRY_BINARY:

			// 開いていたら閉じるよ～
			if (_outFile.is_open())
				_outFile.close();
			break;
		case _NON:
			break;
		}
	}

	bool File::Getline(std::string& outBuf)
	{
		if (!_inputFile.is_open())
			return false;

		return getline(_inputFile, outBuf) ? true : false;
	}

	// =============================================================
	// 入力用ファイルポインタ格納用の取得
	// =============================================================
	std::ifstream& File::GetIfstream(void)
	{
		return _inputFile;
	}

	// =============================================================
	// 出力用ファイルポインタ格納用の取得
	// =============================================================
	std::ofstream& File::GetOfstream(void)
	{
		return _outFile;
	}
}