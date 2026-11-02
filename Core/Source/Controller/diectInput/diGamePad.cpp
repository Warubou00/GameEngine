// --------------------------------------------------------------------
// diGamePad.cpp				// DirectInputのGamePadクラス
// Data   :2024/03/03 (2026/11/02 Core.dll へ入れる時の修正。内容は diGamePad.h)
// Author :Keigo Onari
// DirectInputのGamePadの接続を保証(Switchは入力が謎なため使用は控える)
// --------------------------------------------------------------------

// ==============================================
// インクルード
// ==============================================
#include "diGamePad.h"
#include "diGamePadButtonConfigData.h"
#include "diGamePadButtonA.h"
#include <vector>
#include <cstring>

// ==============================================
// マクロ
// ==============================================
#define RANGE_MIN (-1000)
#define RANGE_MAX (1000)
#define DEAD_ZONE (2500)	// 10000 に対する値 (25%)

// 以前は extern const TCHAR* windowClassName; (main.cpp で定義) を宣言していた。
// 使っていた SetCooperativeLevel はコメントアウト済みで、DLL では定義する所が無いので消した
// (SetCooperativeLevel を呼ばない時は 非排他 + バックグラウンド で読める)

namespace ko {

    namespace
    {
        // ==============================================
        // XInput のパッドの VID / PID の一覧 (RawInput のデバイス名に "IG_" が入っている物)
        // Microsoft の "XInput and DirectInput" の手順 (WMI より軽い RawInput 版)
        // 列挙の始めに 1 回だけ作る
        // ==============================================
        std::vector<DWORD> CollectXInputVidPid(void)
        {
            std::vector<DWORD> result;
            UINT count = 0;
            if (GetRawInputDeviceList(nullptr, &count, sizeof(RAWINPUTDEVICELIST)) != 0 || count == 0)
                return result;

            std::vector<RAWINPUTDEVICELIST> devices(count);
            count = GetRawInputDeviceList(devices.data(), &count, sizeof(RAWINPUTDEVICELIST));
            if (count == (UINT)-1)
                return result;

            for (UINT i = 0; i < count; i++)
            {
                if (devices[i].dwType != RIM_TYPEHID)
                    continue;

                RID_DEVICE_INFO info = {};
                info.cbSize = sizeof(info);
                UINT size = sizeof(info);
                if (GetRawInputDeviceInfoA(devices[i].hDevice, RIDI_DEVICEINFO, &info, &size) == (UINT)-1)
                    continue;

                char name[256] = {};
                size = sizeof(name);
                if (GetRawInputDeviceInfoA(devices[i].hDevice, RIDI_DEVICENAME, name, &size) == (UINT)-1)
                    continue;

                if (strstr(name, "IG_") || strstr(name, "ig_"))
                    result.push_back(MAKELONG(info.hid.dwVendorId, info.hid.dwProductId));
            }
            return result;
        }

        struct EnumContext
        {
            DirectInputGamePad* pad;
            std::vector<DWORD> xinputVidPid;
        };

        // 軸があるか (無い軸は GetDeviceState で 0 が返り、範囲を -1000 ～ 1000 にしていても真ん中に見えない)
        bool HasAxis(IDirectInputDevice8* device, DWORD offset)
        {
            DIDEVICEOBJECTINSTANCE object = {};
            object.dwSize = sizeof(object);
            return SUCCEEDED(device->GetObjectInfo(&object, offset, DIPH_BYOFFSET));
        }

        // POV (1/100 度。真ん中は LOWORD が 0xFFFF) → 十字キーのビット
        DWORD PovToButtons(DWORD pov)
        {
            if (LOWORD(pov) == 0xFFFF)
                return 0;
            // 8 方向。45 度ずつ、真上 (0) から時計回り
            static const DWORD table[8] = {
                BUTTON_UP, BUTTON_UP | BUTTON_RIGHT, BUTTON_RIGHT, BUTTON_DOWN | BUTTON_RIGHT,
                BUTTON_DOWN, BUTTON_DOWN | BUTTON_LEFT, BUTTON_LEFT, BUTTON_UP | BUTTON_LEFT };
            return table[((pov + 2250) / 4500) % 8];
        }

        FLOAT ToAxis(LONG value)
        {
            return (FLOAT)value / RANGE_MAX;
        }
    }

    // ==============================================
    // コールバック関数
    // デバイスを列挙する度呼ばれる
    // ==============================================
    BOOL CALLBACK EnumDevicesCallBackX(LPCDIDEVICEINSTANCE ipddi, LPVOID pvRef)
    {
        EnumContext* context = (EnumContext*)pvRef;
        DirectInputGamePad* pad = context->pad;

        // 以前は数を調べず、5 個目で配列の外に書いていた
        if (pad->GetPadDeviceNum() >= MAX_GAME_PAD)
            return DIENUM_STOP;

        // XInput のパッドは XInput 側で読む (以前は両方で数えて、1 台が 2 台に見えていた)
        for (DWORD vidPid : context->xinputVidPid)
        {
            if (vidPid == ipddi->guidProduct.Data1)
                return DIENUM_CONTINUE;
        }

        // 以前は GetPadDevice (初期化していない配列の値) を temp に入れてから CreateDevice に渡していた
        IDirectInputDevice8* temp = nullptr;
        HRESULT result = DirectInput::getInstance()->GetDevice()->CreateDevice(ipddi->guidInstance, &temp, NULL);
        if (SUCCEEDED(result) && temp)
        {
            pad->SetGamePadDevice(temp, pad->GetPadDeviceNum());
            pad->IncrementPadDeviceNum();
        }
        return DIENUM_CONTINUE;	// 次のデバイスを列挙
    };

    // ==============================================
    // Singleton化
    // ==============================================
    DirectInputGamePad* DirectInputGamePad::getInstance(void)
    {
        // 以前は new したまま解放していなかった
        static DirectInputGamePad instance;
        return &instance;
    }

    // ==============================================
    // 初期化
    // ==============================================
    HRESULT DirectInputGamePad::Init(void)
    {
        IDirectInput8* directInput = DirectInput::getInstance()->GetDevice();
        if (!directInput)
            return E_FAIL;

        // 2 回目 (列挙し直し) は今のパッドを解放してから
        releaseDevices();

        EnumContext context = { this, CollectXInputVidPid() };
        HRESULT hr = directInput->EnumDevices(
            DI8DEVCLASS_GAMECTRL,       // 以前は DI8DEVTYPE_JOYSTICK (ゲームパッドとして登録された物が漏れる)
            EnumDevicesCallBackX,
            &context,
            DIEDFL_ATTACHEDONLY);

        if (FAILED(hr))
        {
            return hr;
        }

        for (int i = 0; i < _gamePadNum; i++)
        {
            // 以前は失敗すると return して、後ろのパッドを放置していた。失敗したパッドは読まないだけにする
            if (FAILED(_gamePadDevice[i]->SetDataFormat(&c_dfDIJoystick)))
                continue;

            setPropaty(i);

            // ジョイスティックの入力開始 (失敗しても Update でもう一度試す)
            _gamePadDevice[i]->Acquire();
            _connected[i] = true;
        }

        return S_OK;
    }

    // ==============================================
    // 1 台の状態を読む。読めなければ false (状態は 0 にする)
    // ==============================================
    bool DirectInputGamePad::readState(int index)
    {
        IDirectInputDevice8* device = _gamePadDevice[index];

        // 以前は Poll / GetDeviceState が失敗すると、DIERR_INPUTLOST の間ずっと Acquire を繰り返していた
        // (抜いたままだと抜けられない)。1 フレーム 1 回だけ Acquire を試す
        HRESULT result = device->Poll();	// ジョイスティックにポールをかける
        if (FAILED(result))
        {
            if (FAILED(device->Acquire()))
            {
                _currDijs[index] = DIJOYSTATE();
                return false;
            }
            device->Poll();
        }

        result = device->GetDeviceState(sizeof(DIJOYSTATE), &_currDijs[index]);	// デバイス状態を読み取る
        if (FAILED(result))
        {
            _currDijs[index] = DIJOYSTATE();
            return false;
        }
        return true;
    }

    // ==============================================
    // 更新
    // ==============================================
    void DirectInputGamePad::Update(void)
    {
        const DirectInputGamePadButtonConfigData* config = DirectInputGamePadButtonConfigData::getInstance();

        for (int i = 0; i < _gamePadNum; i++)
        {
            const DWORD lastPadState = _padState[i];
            _padState[i] = 0x00000000l;	// 初期化

            _connected[i] = readState(i);
            if (!_connected[i])
            {
                _padTrigger[i] = 0;
                continue;
            }

            const BYTE* buttons = _currDijs[i].rgbButtons;
            auto pressed = [&](DirectInputButtonType type) {
                const int button = config->Get(type);
                return button >= 0 && button < 32 && (buttons[button] & 0x80) != 0;
            };

            if (pressed(_BUTTON_A))         _padState[i] |= BUTTON_A;
            if (pressed(_BUTTON_B))         _padState[i] |= BUTTON_B;
            if (pressed(_BUTTON_X))         _padState[i] |= BUTTON_X;
            if (pressed(_BUTTON_Y))         _padState[i] |= BUTTON_Y;
            if (pressed(_BUTTON_L))         _padState[i] |= BUTTON_L;
            if (pressed(_BUTTON_L_TRIGGER)) _padState[i] |= BUTTON_L2;
            if (pressed(_BUTTON_R))         _padState[i] |= BUTTON_R;
            if (pressed(_BUTTON_R_TRIGGER)) _padState[i] |= BUTTON_R2;
            if (pressed(_BUTTON_START))     _padState[i] |= BUTTON_START;
            if (pressed(_BUTTON_SELECT))    _padState[i] |= BUTTON_SELECT;
            if (pressed(_BUTTON_L_THUMB))   _padState[i] |= BUTTON_L3;
            if (pressed(_BUTTON_R_THUMB))   _padState[i] |= BUTTON_R3;
            _padState[i] |= PovToButtons(_currDijs[i].rgdwPOV[0]);

            // Trigger設定
            _padTrigger[i] = ((lastPadState ^ _padState[i])	// 前回と違っていて
                & _padState[i]);					        // しかも今ONのやつ
        }
    }

    // ==============================================
    // キーコンフィグを始める
    // ==============================================
    void DirectInputGamePad::BeginKeyConfig(void)
    {
        delete _pDigpb;
        _pDigpb = new DirectInputGamePadButtonA;
        _isInitGamePad = true;
        _configStep = 0;
        _configPad = -1;	// 最初の UpdateSetKeyConfig で前の値を今の値にそろえる
    }

    // ==============================================
    // キーコンフィグのUpdate処理
    // ==============================================
    void DirectInputGamePad::UpdateSetKeyConfig(int index)
    {
        if (!_isInitGamePad || !_pDigpb || index < 0 || index >= _gamePadNum)
            return;

        _connected[index] = readState(index);

        // 始めた直後 / パッドが替わった時は、押しっぱなしのボタンを「押した瞬間」としない
        if (_configPad != index)
        {
            _configPad = index;
            _prevDijs = _currDijs[index];
            return;
        }

        // Check の中で _pDigpb が次の状態に置き換わる (古い状態は delete される)
        _pDigpb->Check(this);
        _prevDijs = _currDijs[index];

        if (!_isInitGamePad)
        {
            // 終わった (DirectInputGamePadButtonEND)。状態は要らないので消す
            delete _pDigpb;
            _pDigpb = nullptr;
        }
    }

    // ==============================================
    // 入力取得処理
    // アナログパッドX軸取得
    // 以前は index を使わず、最後に読んだパッドの値を返していた
    // ==============================================
    FLOAT DirectInputGamePad::GetAnalogPadX(int index)
    {
        return IsConnected(index) ? ToAxis(_currDijs[index].lX) : 0.0f;
    }

    // ==============================================
    // 入力取得処理
    // アナログパッドY軸取得
    // ==============================================
    FLOAT DirectInputGamePad::GetAnalogPadY(int index)
    {
        return IsConnected(index) ? -ToAxis(_currDijs[index].lY) : 0.0f;
    }

    // ==============================================
    // 右スティック X (Z 軸)
    // ==============================================
    FLOAT DirectInputGamePad::GetAnalogPadRX(int index)
    {
        return IsConnected(index) && _hasRightStick[index] ? ToAxis(_currDijs[index].lZ) : 0.0f;
    }

    // ==============================================
    // 右スティック Y (Rz 軸)
    // ==============================================
    FLOAT DirectInputGamePad::GetAnalogPadRY(int index)
    {
        return IsConnected(index) && _hasRightStick[index] ? -ToAxis(_currDijs[index].lRz) : 0.0f;
    }

    // ==============================================
    // トリガー (Rx = L2 / Ry = R2。-1000 ～ 1000 → 0 ～ 1)
    // ==============================================
    FLOAT DirectInputGamePad::GetTrigger(int index, bool right)
    {
        if (!IsConnected(index))
            return 0.0f;

        if (_hasAnalogTrigger[index])
        {
            const LONG value = right ? _currDijs[index].lRy : _currDijs[index].lRx;
            return (FLOAT)(value - RANGE_MIN) / (RANGE_MAX - RANGE_MIN);
        }
        return (_padState[index] & (right ? BUTTON_R2 : BUTTON_L2)) ? 1.0f : 0.0f;
    }

    // ==============================================
    // 入力取得処理
    // 連続
    // ==============================================
    bool DirectInputGamePad::GetButton(int index, DWORD button)
    {
        return isValid(index) && (button & _padState[index]) != 0;
    }

    // ==============================================
    // 入力取得処理
    // 押した瞬間のみ
    // ==============================================
    bool DirectInputGamePad::GetButtonDown(int index, DWORD button)
    {
        return isValid(index) && (button & _padTrigger[index]) != 0;
    }

    // ==============================================
    // パッドだけ解放 (列挙し直しの前にも使う)
    // ==============================================
    void DirectInputGamePad::releaseDevices(void)
    {
        for (int i = 0; i < MAX_GAME_PAD; i++)
        {
            if (_gamePadDevice[i])
            {
                _gamePadDevice[i]->Unacquire();
                _gamePadDevice[i]->Release();
                _gamePadDevice[i] = nullptr;
            }
            _padState[i] = 0;
            _padTrigger[i] = 0;
            _connected[i] = false;
            _hasRightStick[i] = false;
            _hasAnalogTrigger[i] = false;
            _currDijs[i] = DIJOYSTATE();
        }
        _gamePadNum = 0;
    }

    // ==============================================
    // 破棄
    // ==============================================
    void DirectInputGamePad::Release(void)
    {
        releaseDevices();

        // 以前はキーコンフィグの状態を解放していなかった
        delete _pDigpb;
        _pDigpb = nullptr;
        _isInitGamePad = false;
    }

    // ==============================================
    // デバイスのプロパティ設定
    // ==============================================
    HRESULT DirectInputGamePad::setPropaty(int index)
    {
        IDirectInputDevice8* device = _gamePadDevice[index];

        // 以前は X / Y だけ範囲を決めていた。右スティック (Z / Rz) とトリガー (Rx / Ry) は、あれば同じ範囲にする
        _hasRightStick[index] = HasAxis(device, DIJOFS_Z) && HasAxis(device, DIJOFS_RZ);
        _hasAnalogTrigger[index] = HasAxis(device, DIJOFS_RX) && HasAxis(device, DIJOFS_RY);

        DIPROPRANGE				diprg;
        ZeroMemory(&diprg, sizeof(diprg));
        diprg.diph.dwSize = sizeof(diprg);
        diprg.diph.dwHeaderSize = sizeof(diprg.diph);
        diprg.diph.dwHow = DIPH_BYOFFSET;
        diprg.lMin = RANGE_MIN;
        diprg.lMax = RANGE_MAX;

        const DWORD axes[] = { DIJOFS_X, DIJOFS_Y, DIJOFS_Z, DIJOFS_RZ, DIJOFS_RX, DIJOFS_RY };
        for (DWORD axis : axes)
        {
            diprg.diph.dwObj = axis;
            device->SetProperty(DIPROP_RANGE, &diprg.diph);	// 無い軸は失敗するだけ
        }

        // 各軸ごとに、無効のゾーン値を設定する。
        // 無効ゾーンとは、中央からの微少なジョイスティックの動きを無視する範囲のこと。
        // 指定する値は、10000に対する相対値(2000なら20パーセント)。トリガー (Rx / Ry) には付けない
        DIPROPDWORD				dipdw;
        dipdw.diph.dwSize = sizeof(DIPROPDWORD);
        dipdw.diph.dwHeaderSize = sizeof(dipdw.diph);
        dipdw.diph.dwHow = DIPH_BYOFFSET;
        dipdw.dwData = DEAD_ZONE;

        const DWORD sticks[] = { DIJOFS_X, DIJOFS_Y, DIJOFS_Z, DIJOFS_RZ };
        for (DWORD axis : sticks)
        {
            dipdw.diph.dwObj = axis;
            device->SetProperty(DIPROP_DEADZONE, &dipdw.diph);
        }

        return S_OK;
    }
}
