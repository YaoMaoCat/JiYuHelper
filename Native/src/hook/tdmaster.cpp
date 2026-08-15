#include "tdmaster.hpp"
#include "engine.hpp"
#include "../core/log.hpp"

// Hook LibTDMaster.dll exported functions for INPUT CONTROL only.
// (Network simulation hooks are in bypass_master.dll for MasterHelper.exe)
// 
// IMPORTANT: LockLocalInput sets internal state that UnLockLocalInput
// depends on. If we no-op LockLocalInput, UnLockLocalInput hangs/crashes.
// 
// Solution: Let LockLocalInput run (its hook-based input blocking is
// already nullified by our keyboard.cpp hook), but block the higher-level
// "hook to remote" function so input stays local.

typedef void (__cdecl* LockLocalInput_t)();
static LockLocalInput_t Real_LockLocalInput = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetTDMasterEnabled(bool enable) {
    g_enabled = enable;
    
}

static void __cdecl ProxyLockLocalInput() {
    if (!g_enabled) { if (Real_LockLocalInput) Real_LockLocalInput(); return; }
    Log("[TDM] LockLocalInput() - allowing (hooks already nullified)");
    if (Real_LockLocalInput) Real_LockLocalInput();
    // Real function runs but its SetWindowsHookEx calls are intercepted
    // by our keyboard.cpp (WH_KEYBOARD_LL + WH_MOUSE_LL → NullHook)
}

typedef void (__cdecl* UnLockLocalInput_t)();
static UnLockLocalInput_t Real_UnLockLocalInput = nullptr;

static void __cdecl ProxyUnLockLocalInput() {
    Log("[TDM] UnLockLocalInput() passthrough");
    if (Real_UnLockLocalInput) Real_UnLockLocalInput();
}

// HookLocalInputToRemoteHost - block this so input doesn't get
// captured and forwarded to the teacher via SendInput.
typedef void (__cdecl* HookLocalInputToRemoteHost_t)();
static HookLocalInputToRemoteHost_t Real_HookLocalInputToRemoteHost = nullptr;

static void __cdecl ProxyHookLocalInputToRemoteHost() {
    if (!g_enabled) return; // don't block when disabled
    Log("[TDM] Blocked HookLocalInputToRemoteHost() - input stays local");
    // Don't call real = input not forwarded to teacher
}

// UnHookLocalInput - paired with above
typedef void (__cdecl* UnHookLocalInput_t)();
static UnHookLocalInput_t Real_UnHookLocalInput = nullptr;

static void __cdecl ProxyUnHookLocalInput() {
    Log("[TDM] UnHookLocalInput() passthrough");
    if (Real_UnHookLocalInput) Real_UnHookLocalInput();
}

typedef void (__cdecl* EnableCtrlAltDel_t)(BOOL);
static EnableCtrlAltDel_t Real_EnableCtrlAltDel = nullptr;

static void __cdecl ProxyEnableCtrlAltDel(BOOL enable) {
    Log("[TDM] EnableCtrlAltDel(%d) passthrough", enable);
    if (Real_EnableCtrlAltDel) Real_EnableCtrlAltDel(enable);
}

void InstallTDMasterHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "LockLocalInput",
          ProxyLockLocalInput, (void**)&Real_LockLocalInput, "tdm lock" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "UnLockLocalInput",
          ProxyUnLockLocalInput, (void**)&Real_UnLockLocalInput, "tdm unlock" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "HookLocalInputToRemoteHost",
          ProxyHookLocalInputToRemoteHost, (void**)&Real_HookLocalInputToRemoteHost, "tdm hook remote" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "UnHookLocalInput",
          ProxyUnHookLocalInput, (void**)&Real_UnHookLocalInput, "tdm unhook remote" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "EnableCtrlAltDel",
          ProxyEnableCtrlAltDel, (void**)&Real_EnableCtrlAltDel, "tdm cad" },
    };
    InstallHooks(hooks);
    Log("[TDM] LibTDMaster input hooks installed");
}
