// --------------------------------------------------------------------
// diGamePadButtonSelect.cpp	// DirectInputのGamePadのセレクトボタンを扱うクラス
// Data   :2024/03/04
// Author :Keigo Onari
// ストラテジーパターンにより実装
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "diGamePad.h"
#include "diGamePadButtonSelect.h"
#include "diGamePadButtonEnd.h"
#include "diGamePadButtonConfigData.h"

namespace ko {
	void DirectInputGamePadButtonSelect::Check(DirectInputGamePad* pInstance)
	{
		for (int i = 0; i < DI_FOR_SENTENCE_NUM; i++)
		{
			// ボタン押されたら8bit目が1(10進数で128)になるので0と比較し大きければ入力の瞬間があったものとする
			if (pInstance->GetCurrentDirectInputJoyState().rgbButtons[i] > pInstance->GetPreviousDirectInputJoyState().rgbButtons[i])
			{
				DirectInputGamePadButtonConfigData::getInstance()->Set(_BUTTON_SELECT, i);
				pInstance->SetNewGamePadButtonState(new DirectInputGamePadButtonEND);
				break;
			}
		}
	}
}