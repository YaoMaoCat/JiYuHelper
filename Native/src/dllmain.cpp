//
// dllmain.cpp -- JiYuHook (bypass_main.dll) entry
// Injected into StudentMain.exe. ALL hook modules are installed up front;
// the runtime enable state is driven by hook.cfg (initial) and by
// "UPDATE|mask" commands over the named pipe \\.\pipe\JYHookHelper
// (hot-update, see core/hotupdate.hpp).
//
#include <windows.h>
#include <cstdlib>
#include <string>
#include "../config/config.hpp"
#include "core/log.hpp"
#include "core/pipe.hpp"
#include "core/hotupdate.hpp"
#include "hook/keyboard.hpp"
#include "hook/topmost.hpp"
#include "hook/applist.hpp"
#include "hook/proclist.hpp"
#include "hook/screen.hpp"
#include "hook/remote.hpp"
#include "hook/screencap.hpp"
#include "hook/tdmaster.hpp"
#include "hook/focuslock.hpp"
#include "hook/procguard.hpp"
#include "hook/filterguard.hpp"
#include "hook/netfilterguard.hpp"
#include "monitor/monitor.hpp"

// ---- inbound pipe command: UPDATE|0x<mask> / PING / SCREEN_RELOAD ----
static void OnPipeCommand(const char* cmd) {
    if (strncmp(cmd, "UPDATE|", 7) == 0) {
        uint64_t mask = strtoull(cmd + 7, nullptr, 0);
        HotUpdate(mask);
        PipeSend("INFO", "Hot-updated: mask=0x%llX", mask);
    } else if (strcmp(cmd, "PING") == 0) {
        PipeSend("HEARTBEAT", "pong");
    } else if (strcmp(cmd, "SCREEN_RELOAD") == 0) {
        ReloadScreenFake();
        PipeSend("INFO", "Fake screen reloaded");
    }
}

// Resolve the hook.cfg path next to THIS DLL (for diagnostics)
static const wchar_t* GetConfigPathForLog() {
    static wchar_t path[MAX_PATH] = {0};
    if (!path[0]) {
        HMODULE self = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCWSTR)(LPVOID)&OnPipeCommand, &self);
        GetModuleFileNameW(self ? self : GetModuleHandleW(nullptr), path, MAX_PATH);
        wchar_t* sep = wcsrchr(path, L'\\');
        if (sep) wcscpy(sep + 1, L"hook.cfg");
    }
    return path;
}

// Apply initial feature mask from hook.cfg (all modules are installed,
// HotUpdate() decides what actually runs). Returns the mask for logging.
static uint64_t ApplyConfigMask() {
    Config& cfg = GetConfig();
    uint64_t mask = 0;
    if (cfg.enableKeyboardBypass) mask |= FEATURE_KEYBOARD;
    if (cfg.enableTopmostBlock)   mask |= FEATURE_TOPMOST | FEATURE_FOCUS;
    if (cfg.enableAppListBlock)   mask |= FEATURE_APPLIST;
    if (cfg.enableProcListBlock)  mask |= FEATURE_PROCLIST | FEATURE_PROCGUARD;
    if (cfg.enableScreenFake)     mask |= FEATURE_SCREENFAKE | FEATURE_SCREENCAP;
    if (cfg.enableRemoteBlock)    mask |= FEATURE_REMOTE | FEATURE_INPUTLOCK | FEATURE_FILTER;
    if (cfg.enableBlackMonitor)   mask |= FEATURE_BLACKMON;
    HotUpdate(mask);
    return mask;
}

// ---- exported: soft stop / restart (safe, no FreeLibrary) ----
// BypassStop: disable all features + stop threads + close pipe, but keep
// the hooks installed (proxies become pass-through via g_enabled=false).
// BypassStart: reload config, restart pipe threads, re-apply feature mask.
// Hard unload (FreeLibrary) is intentionally avoided: restoring MinHook
// patches after JiYu reloaded its own modules can crash the process.
#include <MinHook.h>

static bool g_installed = false;

// 注意: 用 __cdecl (x86 下导出名无修饰, 否则 stdcall 会变成 _BypassStart@0,
// App 端按名字解析导出表会找不到)
extern "C" __declspec(dllexport) void __cdecl BypassStart(void) {
    LoadConfig();
    PipeInit(L"\\\\.\\pipe\\JYHookHelper", OnPipeCommand);

    if (!g_installed) {
        g_installed = true;
        Log("=== JiYuHook install ===");
        InstallKeyboardHook();
        InstallTopmostHook();
        InstallAppListHook();
        InstallProcListHook();
        InstallScreenFakeHook();
        InstallRemoteInputHook();
        InstallTDMasterHook();
        InstallFocusLockHook();
        InstallScreenCapHook();
        InstallProcGuardHook();
        InstallFilterGuardHook();
        InstallNetFilterGuard();
    }

    uint64_t mask = ApplyConfigMask();
    StartMonitor();
    // 网页过滤主动清除由 bypass_master (SYSTEM) 负责; StudentMain 无驱动权限
    Log("=== JiYuHook started (mask=0x%llX) ===", mask);
    PipeSend("LOADED", "JiYuHook started (mask=0x%llX)", mask);
}

extern "C" __declspec(dllexport) void __cdecl BypassStop(void) {
    Log("JiYuHook stop requested");
    HotUpdate(0);      // all features off -> proxies pass through
    StopMonitor();
    ReleaseScreenFake(); // unlock screen.png (GDI+ file handle)
    PipeShutdown();
    Log("JiYuHook stopped");
}

// Restore all MinHook patches. SEH-guarded: if a restore crashes (target
// module already gone etc.) we return non-zero and the app keeps the module
// loaded instead of unloading it. Return 0 (MH_OK) on success.
extern "C" __declspec(dllexport) int __cdecl BypassUnhook(void) {
    ReleaseScreenFake(); // ensure screen.png is unlocked before unload
    __try {
        return MH_Uninitialize();
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -1;
    }
}

static DWORD WINAPI InitThread(LPVOID) {
    BypassStart();
    return 0;
}

BOOL APIENTRY DllMain(HMODULE mod, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(mod);
        HANDLE h = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
        if (h) CloseHandle(h);
    }
    return TRUE;
}
