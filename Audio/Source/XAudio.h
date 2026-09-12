
/***********************************************************************************
* @file    XAudio.h
* @brief   
* @details 
*
* @author  Keigo Onari
* @date    2026/01/29
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef XAUDIO_H_
#define XAUDIO_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <xaudio2.h>
#include <iostream>
#include <vector>
#pragma comment(lib, "xaudio2.lib")
#pragma comment(lib, "winmm.lib")

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Audio
{

    class XAudio
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/
    public:
        struct CallbackMethodsBase : public IXAudio2VoiceCallback 
        {
        public:

            /// <summary>
            /// オーディオパスの最初に呼ばれる <br/>
            /// 1フレーム分のミキシングが始まる直前
            /// </summary>
            /// <param name="">SourceVoice の場合のみ意味を持つ
            ///「この処理パスで必要な PCM データ量（バイト）」
            /// ストリーミング音声で
            /// 「次にどれくらいデータを供給すべきか」 の目安</param>
            /// 【主な用途】
            /// ストリーミング再生（BGM / 音声データの逐次供給）<br/>
            ///  足りなくなりそうなら次のバッファを用意する」
            void OnVoiceProcessingPassStart(UINT32) override {
                //std::cout << "Pass Start\n";
            }

            /// <summary>
            /// 1回のオーディオ再生が終わったタイミングで呼ばれる
            /// </summary>
            void OnVoiceProcessingPassEnd() override {
                //std::cout << "Pass End\n";
            }

            /// <summary>
            /// 全てのバッファの再生が終わったタイミング
            /// </summary>
            /// 【主な用途】
            /// 再生完了通知
            /// BGM のループ再生開始
            /// SE 再生終了フラグ
            void OnStreamEnd() override {
                std::cout << "Stream End\n";
            }

            /// <summary>
            /// SubmitSourceBufferをしたバッファの再生開始時
            /// </summary>
            /// <param name="pBufferContext">XAUDIO2_BUFFER::pContext に渡したポインタ
            /// ユーザー定義の識別子</param>
            /// 【主な用途】
            /// 「どのバッファが今再生されているか」を知る
            /// デバッグ / ログ / 状態管理
            /// ここがストリーミング作業のメインとなる
            void OnBufferStart(void* pBufferContext) override {
                std::cout << "Buffer Start\n";
            }

            /// <summary>
            /// 1つのバッファ再生が終った時
            /// </summary>
            /// <param name="pBufferContext">開始時と同じものが渡される</param>
            /// 【主な用途】
            /// バッファの解放
            /// 次のストリーミングバッファ投入
            /// 再生キュー管理
            void OnBufferEnd(void* pBufferContext) override {
                // バッファ再生終了 → 次のバッファを供給する
                std::cout << "Buffer finished\n";
            }

            /// <summary>
            /// ループ指定されたバッファのループ再生が終わったとき <br/>
            /// XAUDIO2_BUFFER::LoopCount を指定している場合のみ
            /// </summary>
            /// <param name="pBufferContext">ループ指定されたバッファのループ再生が終わったとき
            /// XAUDIO2_BUFFER::LoopCount を指定している場合のみ</param>
            /// 【主な用途】
            /// ループ回数管理
            /// ループ終了後の処理切り替え
            /// あくまでフラグを立てるだけ -> 例外処理等はメインスレッドで
            void OnLoopEnd(void* pBufferContext) override {
                std::cout << "Loop End\n";
            }

            /// <summary>
            /// 音声再生中にエラーが発生したとき
            /// </summary>
            /// <param name="pBufferContext">エラーが起きたバッファ</param>
            /// <param name="Error">HRESULT（XAUDIO2_E_* 系）</param>
            /// 【主な用途】
            /// エラーログ
            /// 再生停止処理
            /// フォールバック処理
            void OnVoiceError(void* pBufferContext, HRESULT Error) override {
                std::cerr << "Voice error: " << Error << std::endl;
            }
        };

        struct WaveAudioData
        {
        public:
            WAVEFORMATEX waveFormat{};
            std::vector<short> pcmData{};
            BYTE* buffer = nullptr;
            DWORD bufferSize = 0;
        };

    public:          /*---コンストラクタ・デストラクタ---*/
        XAudio() {}
        ~XAudio() {}


    public:          /*---クラスメソッド---*/


        /***********************************************************************************
         * @brief 初期化
         * @details 
         *
         * @return bool 成功の有無
         ************************************************************************************/
        bool Initialize();


        /***********************************************************************************
         * @brief ソースボイスの生成
         * @details 
         *
         * @param[in] const WAVEFORMATEX& waveFormat Waveファイルフォーマット
         * @param[in] XAudio::CallbackMethodsBase* callback
         * @return IXAudio2SouceVoice* ソースボイスオブジェクトのポインタ
         ************************************************************************************/
        IXAudio2SourceVoice* CreateSourceVoiceWAV(const WAVEFORMATEX& waveFormat, XAudio::CallbackMethodsBase* callback);
        

        /***********************************************************************************
         * @brief AudioBufferの作成
         * @details
         *
         * @param[in] BYTE* 生データ
         * @param[in] DWORD トータルのデータサイズ
         * @param[in] int channels　チャンネル数
         * @param[in] WORD nBlockAlign
         * @return std::vector<XAUDIO2_BUFFER> バッファー
         ************************************************************************************/
        std::vector<XAUDIO2_BUFFER> CreateAudioBuffers(BYTE* pAudioData, DWORD totalDataSizeBytes, int channels, WORD nBlockAlign);


        /***********************************************************************************
         * @brief vector<short>からBYTE**へ変更
         * @details
         *
         * @param[in] std::vector<short>& data vector配列PCMデータ
         * @param[in] BYTE** outFrontPointer BYTE*の先頭ポインタ
         * @param[in] DWORD* outBufferSize DWORDの先頭ポインタ
         * @return std::vector<XAUDIO2_BUFFER> バッファー
         ************************************************************************************/
        void ProcessWaveBuffer(std::vector<short>& data, BYTE** outFrontPointer, DWORD* outBufferSize);

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/
        IXAudio2* _pXAudio2 = nullptr;
        IXAudio2MasteringVoice* _pMasterVoice = nullptr;
    };

}


#endif // XAUDIO_H_

/************************************
* End of file
************************************/
