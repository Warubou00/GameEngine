/***********************************************************************************
* @file    crashHandler.cpp
* @brief   クラッシュレポート (v1.8) : C++ 側のクラッシュのミニダンプと原因のテキストを書く
* @details 説明は crashHandler.h
*          落ちたスレッドでは何も作らない (イベントを立てて待つだけ)。書くのは全部、先に作っておいた書くスレッド
*          (落ちたスレッドはスタックが残っていない / ヒープの鍵を持ったまま、のことがある)
*          dbghelp.dll (Windows に入っている) を使う : MiniDumpWriteDump / StackWalk64 / SymFromAddr
*
* @author  Keigo Onari
* @date    2026/11/12
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "crashHandler.h"

#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <DbgHelp.h>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#pragma comment(lib, "dbghelp.lib")

namespace
{
	constexpr DWORD ManagedExceptionCode = 0xE0434352;   // C# の例外 (CLR が投げる SEH の例外。C# の CrashReporter が書く)
	constexpr int   MaxStackFrames       = 64;

	struct State
	{
		std::wstring root;
		std::wstring sessionLog;
		std::wstring appName;
		std::mutex   infoLock;
		std::vector<std::pair<std::wstring, std::wstring>> info;

		LPTOP_LEVEL_EXCEPTION_FILTER previousFilter = nullptr;
		PVOID  vectored     = nullptr;   // AddVectoredExceptionHandler (C# から呼んだ C++ の中で落ちた時用)
		HANDLE requestEvent = nullptr;   // 落ちたスレッド → 書くスレッド
		HANDLE doneEvent    = nullptr;   // 書き終わった
		HANDLE writer       = nullptr;   // 書くスレッド
		bool   installed    = false;
		std::atomic<bool> quit { false };
		std::atomic<bool> handled { false };   // C# か、ここで 1 回書いた
		std::atomic<LONG> crashing { 0 };

		// 落ちた時の情報 (落ちたスレッドが入れて、書くスレッドが読む)
		EXCEPTION_POINTERS* pending      = nullptr;
		DWORD               crashThread  = 0;
		wchar_t             reportFolder[MAX_PATH] = {};
	};

	State& state()
	{
		static State s;
		return s;
	}

	// ---------- 文字列 ----------
	std::string toUtf8(const std::wstring& text)
	{
		if (text.empty()) return {};
		const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
		std::string result(static_cast<size_t>(size), '\0');
		WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), &result[0], size, nullptr, nullptr);
		return result;
	}

	std::string format(const char* fmt, ...)
	{
		char buffer[1024];
		va_list args;
		va_start(args, fmt);
		vsnprintf_s(buffer, sizeof(buffer), _TRUNCATE, fmt, args);
		va_end(args);
		return buffer;
	}

	const char* exceptionName(DWORD code)
	{
		switch (code)
		{
		case EXCEPTION_ACCESS_VIOLATION:         return "ACCESS_VIOLATION";
		case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:    return "ARRAY_BOUNDS_EXCEEDED";
		case EXCEPTION_DATATYPE_MISALIGNMENT:    return "DATATYPE_MISALIGNMENT";
		case EXCEPTION_FLT_DIVIDE_BY_ZERO:       return "FLT_DIVIDE_BY_ZERO";
		case EXCEPTION_FLT_INVALID_OPERATION:    return "FLT_INVALID_OPERATION";
		case EXCEPTION_ILLEGAL_INSTRUCTION:      return "ILLEGAL_INSTRUCTION";
		case EXCEPTION_IN_PAGE_ERROR:            return "IN_PAGE_ERROR";
		case EXCEPTION_INT_DIVIDE_BY_ZERO:       return "INT_DIVIDE_BY_ZERO";
		case EXCEPTION_PRIV_INSTRUCTION:         return "PRIV_INSTRUCTION";
		case EXCEPTION_STACK_OVERFLOW:           return "STACK_OVERFLOW";
		case EXCEPTION_NONCONTINUABLE_EXCEPTION: return "NONCONTINUABLE_EXCEPTION";
		case EXCEPTION_BREAKPOINT:               return "BREAKPOINT";
		case 0xE06D7363:                         return "C++ exception (throw が catch されなかった)";
		case 0xC0000409:                         return "STACK_BUFFER_OVERRUN / FAST_FAIL";
		case 0xC0000374:                         return "HEAP_CORRUPTION";
		default:                                 return "unknown";
		}
	}

	// アドレス → "Module.dll+0x1234"
	std::string moduleOffset(DWORD64 address)
	{
		HMODULE module = nullptr;
		if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		                        reinterpret_cast<LPCWSTR>(address), &module) || !module)
		{
			return "(no module : JIT code?)";
		}
		wchar_t path[MAX_PATH] = {};
		GetModuleFileNameW(module, path, MAX_PATH);
		const wchar_t* name = wcsrchr(path, L'\\');
		return format("%s+0x%llX", toUtf8(name ? name + 1 : path).c_str(), address - reinterpret_cast<DWORD64>(module));
	}

	std::wstring dateFolderName()
	{
		SYSTEMTIME t;
		GetLocalTime(&t);
		wchar_t name[64];
		swprintf_s(name, L"Crash_%04u%02u%02u_%02u%02u%02u", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
		return name;
	}

	// root/Crash_日時 を作る (同じ名前があれば _2, _3 ...)
	std::wstring createReportFolder(const std::wstring& root)
	{
		CreateDirectoryW(root.c_str(), nullptr);
		const std::wstring baseName = root + L"\\" + dateFolderName();
		std::wstring folder = baseName;
		for (int i = 2; !CreateDirectoryW(folder.c_str(), nullptr); ++i)
		{
			if (GetLastError() != ERROR_ALREADY_EXISTS || i > 99) return {};
			folder = baseName + L"_" + std::to_wstring(i);
		}
		return folder;
	}

	bool writeText(const std::wstring& file, const std::string& text)
	{
		HANDLE handle = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		if (handle == INVALID_HANDLE_VALUE) return false;
		static const unsigned char bom[] = { 0xEF, 0xBB, 0xBF };
		DWORD written = 0;
		WriteFile(handle, bom, sizeof(bom), &written, nullptr);
		WriteFile(handle, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
		CloseHandle(handle);
		return true;
	}

	// ---------- スタック (落ちたスレッドの CONTEXT から辿る。PDB があれば関数名と行も) ----------
	std::string stackTrace(HANDLE process, HANDLE thread, const CONTEXT& crashContext)
	{
		std::string text;
		CONTEXT context = crashContext;   // StackWalk64 は書き換えるので写しを使う
		STACKFRAME64 frame = {};
		frame.AddrPC.Offset    = context.Rip;
		frame.AddrPC.Mode      = AddrModeFlat;
		frame.AddrFrame.Offset = context.Rbp;
		frame.AddrFrame.Mode   = AddrModeFlat;
		frame.AddrStack.Offset = context.Rsp;
		frame.AddrStack.Mode   = AddrModeFlat;

		alignas(SYMBOL_INFO) char symbolBuffer[sizeof(SYMBOL_INFO) + 256] = {};
		auto* symbol = reinterpret_cast<SYMBOL_INFO*>(symbolBuffer);
		for (int i = 0; i < MaxStackFrames; ++i)
		{
			if (!StackWalk64(IMAGE_FILE_MACHINE_AMD64, process, thread, &frame, &context, nullptr,
			                 SymFunctionTableAccess64, SymGetModuleBase64, nullptr)) break;
			const DWORD64 address = frame.AddrPC.Offset;
			if (address == 0) break;

			// C# のフレーム (JIT のコード) は dbghelp では辿れない (この先は値がでたらめになる) ので、そこで止める
			HMODULE module = nullptr;
			if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
			                        reinterpret_cast<LPCWSTR>(address), &module))
			{
				text += format("  #%02d 0x%016llX (managed / JIT code : the rest of the stack is in crash.dmp, open it in Visual Studio)\r\n", i, address);
				break;
			}

			std::string line = format("  #%02d 0x%016llX %s", i, address, moduleOffset(address).c_str());
			symbol->SizeOfStruct = sizeof(SYMBOL_INFO);
			symbol->MaxNameLen   = 255;
			DWORD64 displacement = 0;
			if (SymFromAddr(process, address, &displacement, symbol)) line += format("  %s+0x%llX", symbol->Name, displacement);
			IMAGEHLP_LINE64 source = {};
			source.SizeOfStruct = sizeof(source);
			DWORD lineDisplacement = 0;
			if (SymGetLineFromAddr64(process, address, &lineDisplacement, &source)) line += format("  (%s:%lu)", source.FileName, source.LineNumber);
			text += line + "\r\n";
		}
		return text;
	}

	// ---------- 書く (書くスレッドで) ----------
	void writeReport(EXCEPTION_POINTERS* exception, DWORD crashThreadId)
	{
		State& s = state();
		const std::wstring folder = createReportFolder(s.root);
		if (folder.empty()) return;
		wcsncpy_s(s.reportFolder, folder.c_str(), _TRUNCATE);

		HANDLE process = GetCurrentProcess();
		HANDLE thread  = OpenThread(THREAD_ALL_ACCESS, FALSE, crashThreadId);

		// ① ミニダンプ (Visual Studio で開くと、落ちた所のスタックと変数が見える)
		const std::wstring dumpFile = folder + L"\\crash.dmp";
		HANDLE dump = CreateFileW(dumpFile.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
		bool dumpWritten = false;
		if (dump != INVALID_HANDLE_VALUE)
		{
			MINIDUMP_EXCEPTION_INFORMATION info = {};
			info.ThreadId          = crashThreadId;
			info.ExceptionPointers = exception;
			info.ClientPointers    = FALSE;
			const auto type = static_cast<MINIDUMP_TYPE>(MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithThreadInfo |
			                                             MiniDumpWithUnloadedModules | MiniDumpWithHandleData);
			dumpWritten = MiniDumpWriteDump(process, GetCurrentProcessId(), dump, type, exception ? &info : nullptr, nullptr, nullptr) != FALSE;
			CloseHandle(dump);
		}

		// ② 原因のテキスト
		std::string text;
		text += "==== Native crash report ====\r\n";
		text += "App       : " + toUtf8(s.appName) + "\r\n";
		SYSTEMTIME t;
		GetLocalTime(&t);
		text += format("Time      : %04u-%02u-%02u %02u:%02u:%02u\r\n", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond);
		text += format("Process   : %lu  Thread : %lu\r\n", GetCurrentProcessId(), crashThreadId);
		if (exception && exception->ExceptionRecord)
		{
			const EXCEPTION_RECORD& record = *exception->ExceptionRecord;
			text += format("Exception : 0x%08lX %s\r\n", record.ExceptionCode, exceptionName(record.ExceptionCode));
			const auto address = reinterpret_cast<DWORD64>(record.ExceptionAddress);
			text += format("Address   : 0x%016llX %s\r\n", address, moduleOffset(address).c_str());
			if (record.ExceptionCode == EXCEPTION_ACCESS_VIOLATION && record.NumberParameters >= 2)
			{
				const char* operation = record.ExceptionInformation[0] == 0 ? "read" : record.ExceptionInformation[0] == 1 ? "write" : "execute";
				text += format("Access    : %s at 0x%016llX%s\r\n", operation, static_cast<DWORD64>(record.ExceptionInformation[1]),
				               record.ExceptionInformation[1] < 0x10000 ? " (null pointer?)" : "");
			}
		}
		// C# が知らせた情報 (シーン / 再生中 等)。書いている途中で落ちて鍵が取れない時は飛ばす
		if (s.infoLock.try_lock())
		{
			for (const auto& item : s.info) text += toUtf8(item.first) + " : " + toUtf8(item.second) + "\r\n";
			s.infoLock.unlock();
		}
		text += format("Dump      : %s\r\n", dumpWritten ? "crash.dmp" : "(failed)");

		if (exception && exception->ContextRecord && thread)
		{
			// シンボル : exe と DLL のフォルダの PDB だけ (シンボルサーバー (ネット) は使わない)
			wchar_t exePath[MAX_PATH] = {};
			GetModuleFileNameW(nullptr, exePath, MAX_PATH);
			if (wchar_t* slash = wcsrchr(exePath, L'\\')) *slash = L'\0';
			SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS);
			const bool symbols = SymInitializeW(process, exePath, TRUE) != FALSE;
			text += "\r\nStack (crashed thread) :\r\n";
			text += stackTrace(process, thread, *exception->ContextRecord);
			if (symbols) SymCleanup(process);
		}
		writeText(folder + L"\\native.txt", text);

		// ③ このセッションのログ (C# が行ごとに書いている物)
		if (!s.sessionLog.empty()) CopyFileW(s.sessionLog.c_str(), (folder + L"\\log.txt").c_str(), FALSE);
		if (thread) CloseHandle(thread);
	}

	DWORD WINAPI writerThread(LPVOID)
	{
		State& s = state();
		while (true)
		{
			WaitForSingleObject(s.requestEvent, INFINITE);
			if (s.quit) return 0;
			writeReport(s.pending, s.crashThread);
			SetEvent(s.doneEvent);
		}
	}

	// 書くスレッドに頼んで、書き終わるまで待つ (1 回だけ。C# が書いた後も書かない)
	void capture(EXCEPTION_POINTERS* exception)
	{
		State& s = state();
		if (s.handled || s.crashing.exchange(1) != 0) return;
		s.pending     = exception;
		s.crashThread = GetCurrentThreadId();
		SetEvent(s.requestEvent);
		WaitForSingleObject(s.doneEvent, 60 * 1000);
		s.handled = true;
	}

	LONG WINAPI unhandledFilter(EXCEPTION_POINTERS* exception)
	{
		State& s = state();
		const DWORD code = exception && exception->ExceptionRecord ? exception->ExceptionRecord->ExceptionCode : 0;
		// C# の例外は C# の CrashReporter が書く
		if (code != ManagedExceptionCode) capture(exception);
		// 元のフィルター (.NET / WER) にも渡す (いつものエラーの表示や終わり方はそのまま)
		return s.previousFilter ? s.previousFilter(exception) : EXCEPTION_CONTINUE_SEARCH;
	}

	// ---------- C# から呼んだ C++ の中で落ちた時 ----------
	// .NET は、C# のフレームの上 (P/Invoke の先) で起きたアクセス違反 / スタックオーバーフローを、FailFast でそのまま終わらせる
	// (UnhandledExceptionFilter も AppDomain.UnhandledException も呼ばれない。確認で分かった)
	// なので、最初に呼ばれるベクター例外ハンドラーで受け取る。ただし C# の null 参照なども内部ではアクセス違反として起き、
	// .NET が例外に直して続けるので、落ちた場所が「ネイティブの DLL (.NET のランタイム / C# のアセンブリ / OS の基本の DLL 以外)」の時だけにする
	bool isFatalCode(DWORD code)
	{
		switch (code)
		{
		case EXCEPTION_ACCESS_VIOLATION:
		case EXCEPTION_ILLEGAL_INSTRUCTION:
		case EXCEPTION_PRIV_INSTRUCTION:
		case EXCEPTION_INT_DIVIDE_BY_ZERO:
		case EXCEPTION_STACK_OVERFLOW:
		case EXCEPTION_IN_PAGE_ERROR:
		case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
		case EXCEPTION_DATATYPE_MISALIGNMENT:
			return true;
		default:
			return false;
		}
	}

	bool isNativeCrashAddress(const void* address)
	{
		HMODULE module = nullptr;
		if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		                        static_cast<LPCWSTR>(address), &module) || !module)
		{
			return false;   // モジュールの外 = JIT のコード (C# の null 参照などは .NET が例外にする)
		}
		// C# のアセンブリ (ReadyToRun のコードを含む) は .NET が例外にする
		const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
		const auto* nt  = reinterpret_cast<const IMAGE_NT_HEADERS*>(reinterpret_cast<const char*>(module) + dos->e_lfanew);
		if (nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].VirtualAddress != 0) return false;
		// .NET のランタイム (書き込みバリア等で null 参照を起こして例外にする) / 中で受け取る確かめ方をする OS の基本の DLL
		wchar_t path[MAX_PATH] = {};
		GetModuleFileNameW(module, path, MAX_PATH);
		const wchar_t* name = wcsrchr(path, L'\\');
		name = name ? name + 1 : path;
		static const wchar_t* const ignored[] = { L"coreclr.dll", L"clrjit.dll", L"ntdll.dll", L"kernelbase.dll", L"kernel32.dll" };
		for (const wchar_t* ignore : ignored)
		{
			if (_wcsicmp(name, ignore) == 0) return false;
		}
		return true;
	}

	LONG CALLBACK vectoredHandler(EXCEPTION_POINTERS* exception)
	{
		if (exception && exception->ExceptionRecord && isFatalCode(exception->ExceptionRecord->ExceptionCode) &&
		    isNativeCrashAddress(exception->ExceptionRecord->ExceptionAddress))
		{
			capture(exception);
		}
		return EXCEPTION_CONTINUE_SEARCH;   // 受け取るだけ (終わり方は今まで通り)
	}

	// 確認用 (Crash_Test)。最適化で消されないように volatile
	void crashAccessViolation()
	{
		volatile int* pointer = nullptr;
		*pointer = 1;
	}

#pragma warning(push)
#pragma warning(disable : 4717)   // わざと止まらない再帰 (スタックオーバーフローの確認用)
	int recurse(volatile int depth)
	{
		volatile char buffer[4096];
		buffer[0] = static_cast<char>(depth);
		return recurse(depth + 1) + buffer[0];
	}
#pragma warning(pop)
}

namespace ko
{
	bool CrashHandler::Install(const wchar_t* root, const wchar_t* sessionLog, const wchar_t* appName)
	{
		State& s = state();
		if (!root || !*root) return false;
		s.root       = root;
		s.sessionLog = sessionLog ? sessionLog : L"";
		s.appName    = appName ? appName : L"";
		if (s.installed) return true;

		s.requestEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
		s.doneEvent    = CreateEventW(nullptr, TRUE, FALSE, nullptr);
		s.writer       = CreateThread(nullptr, 256 * 1024, writerThread, nullptr, 0, nullptr);
		if (!s.requestEvent || !s.doneEvent || !s.writer) return false;
		SetThreadDescription(s.writer, L"CrashReportWriter");
		// 落ちたスレッドがスタックオーバーフローでも、フィルターを動かせるだけのスタックを残す
		ULONG guarantee = 64 * 1024;
		SetThreadStackGuarantee(&guarantee);
		s.previousFilter = SetUnhandledExceptionFilter(unhandledFilter);
		s.vectored = AddVectoredExceptionHandler(1, vectoredHandler);   // 1 = 一番先に呼ばれる (.NET の物より先)
		s.installed = true;
		return true;
	}

	void CrashHandler::Uninstall()
	{
		State& s = state();
		if (!s.installed) return;
		SetUnhandledExceptionFilter(s.previousFilter);
		if (s.vectored) RemoveVectoredExceptionHandler(s.vectored);
		s.vectored = nullptr;
		s.quit = true;
		SetEvent(s.requestEvent);
		WaitForSingleObject(s.writer, 1000);
		CloseHandle(s.writer);
		CloseHandle(s.requestEvent);
		CloseHandle(s.doneEvent);
		s.writer = s.requestEvent = s.doneEvent = nullptr;
		s.installed = false;
		s.quit = false;
	}

	void CrashHandler::SetInfo(const wchar_t* key, const wchar_t* value)
	{
		if (!key) return;
		State& s = state();
		std::lock_guard<std::mutex> lock(s.infoLock);
		for (auto it = s.info.begin(); it != s.info.end(); ++it)
		{
			if (it->first != key) continue;
			if (value) it->second = value;
			else s.info.erase(it);
			return;
		}
		if (value) s.info.emplace_back(key, value);
	}

	void CrashHandler::SetHandled()
	{
		state().handled = true;
	}

	int32_t CrashHandler::GetReportFolder(wchar_t* buffer, int32_t length)
	{
		const State& s = state();
		const auto size = static_cast<int32_t>(wcslen(s.reportFolder));
		if (size == 0 || !buffer || length <= size) return size;
		wcscpy_s(buffer, static_cast<size_t>(length), s.reportFolder);
		return size;
	}

	void CrashHandler::Test(int32_t kind)
	{
		switch (kind)
		{
		case 1: crashAccessViolation(); break;
		case 2: std::thread(crashAccessViolation).join(); break;
		case 3: recurse(0); break;
		default: break;
		}
	}
}

/************************************
* End of file
************************************/
