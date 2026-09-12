// ==================================================
// file : wavSoundLoader.cpp
// name : Keigo Onari
// data : 2025/09/05
// ver  : v1.00
// 
// https://toolbox.aaa-plaza.net/archives/3559#:~:text=WAV%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%20%E3%81%AF%E3%80%81%20RIFF%20%E3%81%A8%E3%82%88%E3%81%B0%E3%82%8C%E3%82%8B%E5%9E%8B%E5%BC%8F%E3%81%AE%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%E3%81%AE%E4%B8%80%E7%A8%AE%E3%81%A7%E3%81%99%E3%80%82%20RIFF%E3%81%A8%E3%81%AFResource%20Interchange%20File,Format%E3%81%AE%E7%95%A5%E3%81%A7%E3%80%81%E7%94%BB%E5%83%8F%E3%82%84%E9%9F%B3%E5%A3%B0%E3%82%92%E6%A0%BC%E7%B4%8D%E3%81%99%E3%82%8B%E6%B1%8E%E7%94%A8%E3%81%AE%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%E5%BD%A2%E5%BC%8F%E3%81%A7%E3%81%99%E3%80%82%20RIFF%E3%81%AF%E3%80%81%20%E3%83%81%E3%83%A3%E3%83%B3%E3%82%AF%20%EF%BC%88%3D%20chunk%20%E3%80%81%E3%82%82%E3%81%A8%E3%81%AF%E3%80%8E%E5%A4%A7%E3%81%8D%E3%81%AA%E5%A1%8A%E3%80%8F%E3%82%92%E6%84%8F%E5%91%B3%E3%81%99%E3%82%8B%E8%8B%B1%E5%8D%98%E8%AA%9E%EF%BC%89%E3%81%A8%E5%91%BC%E3%81%B0%E3%82%8C%E3%82%8B%E3%83%87%E3%83%BC%E3%82%BF%E3%81%AE%E5%8D%98%E4%BD%8D%E3%81%8C%E4%B8%A6%E3%82%93%E3%81%A0%E6%A7%8B%E9%80%A0%E3%82%92%E3%81%97%E3%81%A6%E3%81%84%E3%81%BE%E3%81%99%E3%80%82%20%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%E5%85%A8%E4%BD%93%E3%81%A8%E3%81%97%E3%81%A6%E3%81%AF%E4%BB%A5%E4%B8%8B%E3%81%AE%E3%82%88%E3%81%86%E3%81%AA%E3%82%A4%E3%83%A1%E3%83%BC%E3%82%B8%E3%81%A7%E3%81%99%E3%80%82
// ==================================================
#include <iostream>
#include <fstream>
#include "wavSoundLoader.h"

namespace Library
{
	namespace Sound
	{
		namespace Loader
		{
			/// <summary>
			/// ロード
			/// </summary>
			/// <param name="filePath"></param>
			/// <returns></returns>
			bool WavLoader::Load(const std::string& filePath)
			{
				std::cerr << "無効な関数です" << std::endl;
				return false;
			}

			std::vector<short> WavLoader::Load(const std::string& filePath, WAVEFORMATEX* wavFormat)
			{
#if false
				std::ifstream file(filePath, std::ios::binary);
				if (!file) {
					std::cerr << "ファイルを開けませんでした: " << filePath << std::endl;
					return false;
				}

				// ヘッダー読み込み
				file.read(reinterpret_cast<char*>(&_riffHeader), sizeof(RIFFHeader));

				while (!file.eof())
				{
					char* chunk = new char[_CHUNK_ID_SIZE] {};
					file.read(reinterpret_cast<char*>(chunk), sizeof(char) * _CHUNK_ID_SIZE);

					if (checkChunk(chunk, _CHUNK_JUNK.c_str()))
					{// データサイズ補正チャンク
						int dummyByteSize;
						file.read(reinterpret_cast<char*>(&dummyByteSize), sizeof(int));
						char* buffer = new char[dummyByteSize];
						file.read(reinterpret_cast<char*>(buffer), sizeof(char) * dummyByteSize);
					}
					else if (checkChunk(chunk, _CHUNK_FMT.c_str()))
					{// フォーマットチャンク
#if false
						// 独自フォーマット読み込み
						file.read(reinterpret_cast<char*>(&_formatChunk), sizeof(FormatChunk));
						if (_formatChunk.fmtChunkSize != _LINEAR_PCM_FMT_CHUNK_SIZE)
						{
							std::cerr << "非圧縮PCMでないため読み込み対応していません" << std::endl;
							return false;
						}
#else
						file.read(reinterpret_cast<char*>(&_format), sizeof(WAVEFORMATEX));

						if (_format.wFormatTag != _LINEAR_PCM_FMT_CHUNK_SIZE)
						{
							std::cerr << "非圧縮PCMでないため読み込み対応していません" << std::endl;
							return false;
						}
#endif
					}
					else if (checkChunk(chunk, _CHUNK_DATA.c_str()))
					{// データチャンク

						file.read(reinterpret_cast<char*>(&_dataChunk), sizeof(DataChunk));

						// PCMデータを読み込み
						std::vector<short> data;
						data.resize(_dataChunk.dataChunkSize / sizeof(short));
						file.read(reinterpret_cast<char*>(data.data()), _dataChunk.dataChunkSize);

						if (!file) {
							std::cerr << "PCMデータの読み込みに失敗しました。" << std::endl;
							_audioData.clear();
							return false;
						}

						// 複数あった場合に末尾に追加していく
						_audioData.insert(_audioData.end(), data.begin(), data.end());
					}

					delete[] chunk;
				}

				return true;
#else
				std::vector<short>audioData(0);

				std::ifstream file(filePath, std::ios::binary);
				if (!file) {
					std::cerr << "ファイルを開けませんでした: " << filePath << std::endl;
					return std::vector<short>(0);
				}

				// 1. RIFFヘッダー読み込み
				RIFFHeader riffHeader;
				if (!file.read(reinterpret_cast<char*>(&riffHeader), sizeof(RIFFHeader))) return audioData;

				// "RIFF" と "WAVE" のチェックを入れたほうが安全です
				if (std::strncmp(riffHeader.chunkID, "RIFF", 4) != 0 ||
					std::strncmp(riffHeader.format, "WAVE", 4) != 0) {
					std::cerr << "有効なWAVファイルではありません。" << std::endl;
					return audioData;
				}

				// 2. チャンク解析ループ
				ChunkHeader chunkHeader;
				while (file.read(reinterpret_cast<char*>(&chunkHeader), sizeof(ChunkHeader)))
				{
					// チャンクIDに応じた処理
					if (std::strncmp(chunkHeader.id, "fmt ", 4) == 0)
					{
						// --- fmt チャンク ---
						// 本来のWAVEFORMATEXサイズよりチャンクサイズが小さい場合のガード
						unsigned int readSize = sizeof(WAVEFORMATEX);
						if (chunkHeader.size < readSize) readSize = chunkHeader.size;

						if (!file.read(reinterpret_cast<char*>(wavFormat), readSize)) return audioData;

						// PCM(1) かつ IEEE Float(3) 以外は対応しないなどのチェック
						if (wavFormat->wFormatTag != WAVE_FORMAT_PCM) { // 1と比較
							std::cerr << "非圧縮PCM以外は対応していません(Tag: " << wavFormat->wFormatTag << ")" << std::endl;
							return audioData;
						}

						// チャンクサイズが構造体より大きかった場合（拡張領域など）、残りをスキップ
						if (chunkHeader.size > readSize) {
							file.seekg(chunkHeader.size - readSize, std::ios::cur);
						}
					}
					else if (std::strncmp(chunkHeader.id, "data", 4) == 0)
					{
						// --- data チャンク ---
						// データサイズ取得
						unsigned int dataBytes = chunkHeader.size;

						// 現在のバッファ末尾に追加する場合のオフセット
						size_t currentSize = audioData.size();
						size_t addCount = dataBytes / sizeof(short);

						// メモリを一回で確保（最適化）
						audioData.resize(currentSize + addCount);

						// 直接ベクターのメモリ領域に書き込む（コピー回避）
						file.read(reinterpret_cast<char*>(audioData.data() + currentSize), dataBytes);

						if (!file) {
							std::cerr << "PCMデータの読み込みに失敗しました。" << std::endl;
							return std::vector<short>(0);
						}

						// 読み込みバイト数が奇数の場合のパディング対応（WAV仕様）
						if (dataBytes % 2 != 0) {
							file.seekg(1, std::ios::cur);
						}
					}
					else
					{
						// --- 未知のチャンク (JUNK, LIST等) ---
						// 読み込まずにサイズ分スキップする（無限ループ回避 & 高速化）
						file.seekg(chunkHeader.size, std::ios::cur);
					}
				}

				return audioData;
#endif
			}

			///// <summary>
			///// フォーマットの取得
			///// </summary>
			///// <returns></returns>
			//const WAVEFORMATEX& WavLoader::GetFormat()const
			//{
			//	return _format;
			//}

			///// <summary>
			///// PCMの取得
			///// </summary>
			///// <returns></returns>
			//const std::vector<short>& WavLoader::GetAudioData() const
			//{
			//	return _audioData;
			//}

			///// <summary>
			///// サンプルレートの取得
			///// </summary>
			///// <returns></returns>
			//unsigned int WavLoader::GetSampleRate() const
			//{
			//	return _format.nSamplesPerSec;
			//}

			///// <summary>
			///// チャンネル数の取得
			///// </summary>
			///// <returns></returns>
			//unsigned short WavLoader::GetNumChannels() const
			//{
			//	return _format.nChannels;
			//}

			///// <summary>
			///// サンプル単位のビット数
			///// </summary>
			///// <returns></returns>
			//unsigned short WavLoader::GetBitsPerSample() const
			//{
			//	return _format.wBitsPerSample;
			//}

			///// <summary>
			///// チャンクのチェック
			///// </summary>
			///// <param name="chunk"></param>
			///// <param name="checkChunk"></param>
			///// <returns></returns>
			//bool WavLoader::checkChunk(const char* chunk, const char* checkChunk)
			//{
			//	return chunk[0] == checkChunk[0] &&
			//		chunk[1] == checkChunk[1] &&
			//		chunk[2] == checkChunk[2] &&
			//		chunk[3] == checkChunk[3];
			//}
		}
	}
}
// ==============================
// end of file
// ==============================