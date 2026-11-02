// --------------------------------------------------------------------
// diGamePadButtonRTrigger.cpp	// DirectInputのGamePadのRトリガーボタンを扱うクラス
// Data   :2024/03/04
// Author :Keigo Onari
// ストラテジーパターンにより実装
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "diGamePad.h"
#include "diGamePadButtonRTrigger.h"
#include "diGamePadButtonStart.h"
#include "diGamePadButtonConfigData.h"

namespace ko {
	void DirectInputGamePadButtonRTrigger::Check(DirectInputGamePad* pInstance)
	{
		for (int i = 0; i < DI_FOR_SENTENCE_NUM; i++)
		{
			// ボタン押されたら8bit目が1(10進数で128)になるので0と比較し大きければ入力の瞬間があったものとする
			if (pInstance->GetCurrentDirectInputJoyState().rgbButtons[i] > pInstance->GetPreviousDirectInputJoyState().rgbButtons[i])
			{
				DirectInputGamePadButtonConfigData::getInstance()->Set(_BUTTON_R_TRIGGER, i);
				pInstance->SetNewGamePadButtonState(new DirectInputGamePadButtonStart);
				break;
			}
		}
	}
}