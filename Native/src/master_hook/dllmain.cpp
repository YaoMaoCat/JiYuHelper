//
// dllmain.cpp -- bypass_master.dll entry
// Injected into MasterHelper.exe to block process termination, spawning,
// and network simulation. Runtime enable state comes from hook.cfg and
// "UPDATE|mask" pipe commands over \\.\pipe\JYMasterHelper.
//
#include <windows.h>
#include <cstdlib>
#include <MinHook.h>
#include "../../config/config.hpp"
#include "../core/log.hpp"
#include "../core/pipe.hpp"
#include "../core/hotupdate.hpp"
#include "../core/netfilter.hpp"
#include "../hook/engine.hpp"
#include "guard.hpp"

// ---- inbound pipe command: UPDATE|0x<mask> / PING ----
static void OnPipeCommand(const char* cmd) {
    if (strncmp(cmd, "UPDATE|", 7) == 0) {
        uint64_t mask = strtoull(cmd + 7, nullptr, 0);
        MasterHotUpdate(mask);
        PipeSend("INFO", "Master hot-updated: mask=0x%llX", mask);
    } else if (strcmp(cmd, "PING") == 0) {
        PipeSend("HEARTBEAT", "pong");
    }
}

// ---- exported: soft stop / restart (no FreeLibrary) ----
static bool g_installed = false;

// 注意: __cdecl, 保证 x86 下导出名无修饰 (_BypassStart@0 问题)
extern "C" __declspec(dllexport) void __cdecl BypassStart(void) {
    LoadConfig();
    PipeInit(L"\\\\.\\pipe\\JYMasterHelper", OnPipeCommand);

    if (!g_installed) {
        g_installed = true;
        Log("=== MasterHelper install ===");
        // 注意: 不要直接调 MH_Initialize() —— engine 的 InstallHooks 内部会初始化,
        // 重复调用会返回 ALREADY_INITIALIZED 导致误判 MinHook init failed, 全部 hook 失效
        InstallMasterProcGuard();
        // SYSTEM 进程才有权限打开 TDNetFilter 驱动设备: 首次注入时清除教师端已配置的网页过滤
        // (之后教师端重新下发规则会被 DeviceIoControl hook 拦截)
        ClearNetFilter();
    }

    Log("=== MasterHelper started ===");
    PipeSend("LOADED", "MasterHelper guard started");
}

extern "C" __declspec(dllexport) void __cdecl BypassStop(void) {
    Log("MasterHelper stop requested");
    MasterHotUpdate(0);
    PipeShutdown();
    Log("MasterHelper stopped");
}

// Restore MinHook patches; SEH-guarded, 0 (MH_OK) = success.
extern "C" __declspec(dllexport) int __cdecl BypassUnhook(void) {
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
