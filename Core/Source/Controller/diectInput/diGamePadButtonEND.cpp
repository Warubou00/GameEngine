// --------------------------------------------------------------------
// diGamePadButtonEND.cpp	// DirectInputのGamePadの最後用
// Data   :2024/03/04
// Author :Keigo Onari
// ストラテジーパターンにより実装
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "diGamePad.h"
#include "diGamePadButtonEND.h"
#include "diGamePadButtonConfigData.h"

namespace ko {

	void DirectInputGamePadButtonEND::Check(DirectInputGamePad* pInstance)
	{

		pInstance->SetInitGamePadFlag(false);

		//int temp[DI_BUTTON_NUM] = {};
		//for (int i = 0; i < DI_BUTTON_NUM; i++)
		//{
		//	temp[i] = DirectInputGamePadButtonConfigData::getInstance()->Get(i);

		//	// 何かしらのキーが押されたら
		//	if (pInstance->GetCurrentDirectInputJoyState().rgbButtons[i] > pInstance->GetPreviousDirectInputJoyState().rgbButtons[i])
		//		pInstance->SetInitGamePadFlag(false);
		//}
	}
}