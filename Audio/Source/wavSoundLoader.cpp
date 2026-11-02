// ==================================================
// file : wavSoundLoader.cpp
// name : Keigo Onari
// data : 2025/09/05
// ver  : v1.10
//
// WAV (RIFF) の構造 : "RIFF" <全体サイズ> "WAVE" の後に、<ID 4byte> <サイズ 4byte> <中身> のチャンクが並ぶ
//   - チャンクのサイズが奇数の時は、後ろに 1 バイトの詰め物が入る (どのチャンクでも)
//   - 必要なのは "fmt " (形式) と "data" (PCM)。それ以外 (LIST / JUNK / bext など) は読み飛ばす
//
// 以前の実装からの主な修正 (詳しくは ClaudLog/Audio_Review_2026-11-02.txt)
//   - 詰め物の処理が data チャンクだけで、奇数サイズの LIST などの後ろでチャンクの位置がずれていた
//   - fmt チャンクが 16 バイト (よくある PCM) の時、WAVEFORMATEX::cbSize が初期化されずに残っていた
//   - data のサイズを信じて確保していたので、壊れた / 途中で切れたファイルで巨大な確保をしていた
//   - PCM が nBlockAlign の倍数でないと XAudio2 が再生を拒否するのに、端数を切っていなかった
//   - std::string のパス (ANSI) で、日本語を含むパスを開けなかった
// ==================================================
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmreg.h>
#include <cstring>
#include <string>
#include "wavSoundLoader.h"
#include "../../Core/Source/Log/logger.h"

namespace
{
	// XAudio2 の 1 バッファの上限 (XAUDIO2_MAX_BUFFER_BYTES = 0x80000000) より小さい物だけ読む
	constexpr uint64_t MAX_FILE_BYTES = 0x7FFFFFFFull;

	bool isChunk(const uint8_t* id, const char* name)
	{
		return std::memcmp(id, name, 4) == 0;
	}

	uint32_t readU32(const uint8_t* p)
	{
		uint32_t value;
		std::memcpy(&value, p, sizeof(value));
		return value;
	}

	void logError(const wchar_t* message, const std::wstring& filePath)
	{
		std::wstring text = L"[WavLoader] ";
		text += message;
		text += L" : ";
		text += filePath;
		Core::Logger::Log(text.c_str());
	}

	// WAVE_FORMAT_EXTENSIBLE の SubFormat が PCM / IEEE float か
	// (KSDATAFORMAT_SUBTYPE_xxx は Data1 が形式の番号で、残りは共通の {xxxxxxxx-0000-0010-8000-00AA00389B71})
	bool isSupportedSubFormat(const GUID& guid, WORD* outTag)
	{
		static const uint8_t baseData4[8] = { 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 };
		if (guid.Data2 != 0x0000 || guid.Data3 != 0x0010 || std::memcmp(guid.Data4, baseData4, 8) != 0) return false;
		if (guid.Data1 != WAVE_FORMAT_PCM && guid.Data1 != WAVE_FORMAT_IEEE_FLOAT) return false;
		*outTag = static_cast<WORD>(guid.Data1);
		return true;
	}
}

namespace Library
{
	namespace Sound
	{
		namespace Loader
		{
			/// <summary>
			/// ロード
			/// 以前は Load(const std::string&) が「無効な関数です」と出して false を返すだけで、
			/// 実際の読み込みは Load(path, WAVEFORMATEX*) が std::vector<short> を返していた
			/// </summary>
			bool WavLoader::Load(const std::wstring& filePath, SoundData* outData)
			{
				if (!outData) return false;

				HANDLE file = CreateFileW(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
					OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
				if (file == INVALID_HANDLE_VALUE)
				{
					logError(L"Failed to open file", filePath);
					return false;
				}

				LARGE_INTEGER size{};
				if (!GetFileSizeEx(file, &size) || size.QuadPart < 12 || static_cast<uint64_t>(size.QuadPart) > MAX_FILE_BYTES)
				{
					CloseHandle(file);
					logError(L"Invalid file size (12 bytes to 2 GB)", filePath);
					return false;
				}

				// ファイル全体を 1 回で読む (PCM はこの配列を指したまま再生するので、別の配列へコピーしない)
				outData->bytes.resize(static_cast<size_t>(size.QuadPart));
				DWORD read = 0;
				const BOOL ok = ReadFile(file, outData->bytes.data(), static_cast<DWORD>(size.QuadPart), &read, nullptr);
				CloseHandle(file);
				if (!ok || read != static_cast<DWORD>(size.QuadPart))
				{
					outData->bytes.clear();
					logError(L"Failed to read file", filePath);
					return false;
				}

				if (!Parse(outData))
				{
					outData->bytes.clear();
					outData->bytes.shrink_to_fit();
					logError(L"Unsupported or broken WAV file", filePath);
					return false;
				}
				return true;
			}

			bool WavLoader::Parse(SoundData* data)
			{
				const uint8_t* bytes    = data->bytes.data();
				const uint64_t fileSize = data->bytes.size();

				// 1. RIFF ヘッダー ("RIFF" <サイズ> "WAVE")
				if (fileSize < 12 || !isChunk(bytes, "RIFF") || !isChunk(bytes + 8, "WAVE")) return false;
				// RIFF のサイズがファイルより大きい (途中で切れた) 時はファイルの終わりまで
				const uint64_t end = (std::min)(fileSize, static_cast<uint64_t>(readU32(bytes + 4)) + 8);

				// 2. チャンクを順に見る
				bool     hasFormat = false;
				bool     hasData   = false;
				uint64_t pos       = 12;
				while (pos + 8 <= end)
				{
					const uint8_t* id        = bytes + pos;
					const uint64_t chunkSize = readU32(bytes + pos + 4);
					const uint64_t body      = pos + 8;
					const uint64_t available = end - body;

					if (isChunk(id, "fmt "))
					{
						// 16 : WAVEFORMAT + wBitsPerSample (PCM)、18 : + cbSize、40 : WAVEFORMATEXTENSIBLE
						if (chunkSize < 16 || chunkSize > available) return false;

						// 以前は sizeof(WAVEFORMATEX) = 18 バイトを読もうとして、16 バイトの時に cbSize が不定のままだった
						const size_t formatSize = static_cast<size_t>((std::max<uint64_t>)(chunkSize, sizeof(WAVEFORMATEX)));
						data->format.assign(formatSize, 0);
						std::memcpy(data->format.data(), bytes + body, static_cast<size_t>(chunkSize));

						auto* format = reinterpret_cast<WAVEFORMATEX*>(data->format.data());
						// cbSize は「後ろに続く拡張部分のバイト数」。チャンクに入りきらない値なら切り詰める
						const size_t extraMax = formatSize - sizeof(WAVEFORMATEX);
						if (chunkSize < sizeof(WAVEFORMATEX)) format->cbSize = 0;
						else if (format->cbSize > extraMax)  format->cbSize = static_cast<WORD>(extraMax);

						WORD tag = format->wFormatTag;
						if (tag == WAVE_FORMAT_EXTENSIBLE)
						{
							// 以前は WAVE_FORMAT_PCM 以外を全部拒否していたので、24bit などの wav が読めなかった
							if (format->cbSize < sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)) return false;
							const auto* extensible = reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(format);
							if (!isSupportedSubFormat(extensible->SubFormat, &tag)) return false;
						}
						if (tag != WAVE_FORMAT_PCM && tag != WAVE_FORMAT_IEEE_FLOAT) return false;
						if (format->nChannels == 0 || format->nSamplesPerSec == 0 || format->nBlockAlign == 0) return false;
						hasFormat = true;
					}
					else if (isChunk(id, "data"))
					{
						if (!hasData)
						{
							// サイズがファイルの残りより大きい (途中で切れた / 0xFFFFFFFF で書かれた) 時は残りの分だけ使う
							// 以前はサイズをそのまま信じて確保し、読み込みに失敗していた
							data->dataOffset = static_cast<uint32_t>(body);
							data->dataSize   = static_cast<uint32_t>((std::min)(chunkSize, available));
							hasData = true;
						}
						// data チャンクは 1 つの決まり。2 つ目以降は使わない (以前は後ろに足していた)
					}
					// それ以外のチャンクは読み飛ばす

					// 次のチャンクへ (奇数サイズの後ろには 1 バイトの詰め物がある。以前は data チャンクの時しか飛ばしていなかった)
					pos = body + chunkSize + (chunkSize & 1);
				}

				if (!hasFormat || !hasData) return false;

				// 3. PCM を 1 フレーム (全チャンネル分) の倍数に切り詰める (端数があると XAudio2 が SubmitSourceBuffer を拒否する)
				const auto* format = reinterpret_cast<const WAVEFORMATEX*>(data->format.data());
				data->dataSize -= data->dataSize % format->nBlockAlign;
				return data->dataSize > 0;
			}
		}
	}
}
// ==============================
// end of file
// ==============================
