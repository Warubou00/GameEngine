// --------------------------------------------------------------------
// di.h					// DirectInputクラス
// Data   :2024/03/03
// Author :Keigo Onari
// DirectInputの基となるデバイスの初期化
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "di.h"

namespace ko {

    // ==============================================
    // Singleton化
    // ==============================================
    DirectInput* DirectInput::getInstance()
    {
        static DirectInput instance;
        return &instance;
    }

    // ==============================================
    // 初期化
    // ==============================================
    HRESULT DirectInput::Init(HINSTANCE hInstance)
    {
        if (_iDevice)
            return S_OK;	// 作成済み

        HRESULT hr;
        hr = DirectInput8Create(hInstance, DIRECTINPUT_VERSION, IID_IDirectInput8, (void**)&_iDevice, nullptr);
        return hr;
    }

    // ==============================================
    // 更新
    // ==============================================
    void DirectInput::Update(void)
    {

    }
}
