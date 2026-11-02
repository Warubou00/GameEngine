// --------------------------------------------------------------------
// diGamePadButtonConfigData.cpp	// DirectInputのrgbButtonのキー情報を格納するクラス
// Data   :2024/03/04
// Author :Keigo Onari
// ストラテジーパターンにより実装
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "diGamePadButtonConfigData.h"

namespace ko {

    DirectInputGamePadButtonConfigData* DirectInputGamePadButtonConfigData::getInstance()
    {
        // 以前は new したまま解放していなかった。関数の中の static にする (DLL を外す時に消える。COM は持っていない)
        static DirectInputGamePadButtonConfigData instance;
        return &instance;
    }
}
