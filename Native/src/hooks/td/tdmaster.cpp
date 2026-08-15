#include "tdmaster.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include <vector>

// Hook LibTDMaster.dll exported functions for INPUT CONTROL only.
// (Network simulation hooks live in master/guard.cpp for MasterHelper.exe)

typedef void (__cdecl* LockLocalInput_t)();
static LockLocalInput_t Real_LockLocalInput = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetTDMasterEnabled(bool enable) {
    g_enabled = enable;
}

static void __cdecl ProxyLockLocalInput() {
    if (!g_enabled) { if (Real_LockLocalInput) Real_LockLocalInput(); return; }
    JY_LOGI("tdmaster", "LockLocalInput() allowed - hooks already nullified");
    if (Real_LockLocalInput) Real_LockLocalInput();
    // real function runs but its SetWindowsHookEx calls are intercepted
    // by the keyboard hook (WH_KEYBOARD_LL + WH_MOUSE_LL -> NullHook)
}

typedef void (__cdecl* UnLockLocalInput_t)();
static UnLockLocalInput_t Real_UnLockLocalInput = nullptr;

static void __cdecl ProxyUnLockLocalInput() {
    JY_LOGD("tdmaster", "UnLockLocalInput() passthrough");
    if (Real_UnLockLocalInput) Real_UnLockLocalInput();
}

// HookLocalInputToRemoteHost - block this so input doesn't get
// captured and forwarded to the teacher via SendInput.
typedef void (__cdecl* HookLocalInputToRemoteHost_t)();
static HookLocalInputToRemoteHost_t Real_HookLocalInputToRemoteHost = nullptr;

static void __cdecl ProxyHookLocalInputToRemoteHost() {
    if (!g_enabled) return; // don't block when disabled
    JY_LOGI("tdmaster", "HookLocalInputToRemoteHost() blocked - input stays local");
    // don't call real = input not forwarded to teacher
}

// UnHookLocalInput - paired with above
typedef void (__cdecl* UnHookLocalInput_t)();
static UnHookLocalInput_t Real_UnHookLocalInput = nullptr;

static void __cdecl ProxyUnHookLocalInput() {
    JY_LOGD("tdmaster", "UnHookLocalInput() passthrough");
    if (Real_UnHookLocalInput) Real_UnHookLocalInput();
}

typedef void (__cdecl* EnableCtrlAltDel_t)(BOOL);
static EnableCtrlAltDel_t Real_EnableCtrlAltDel = nullptr;

static void __cdecl ProxyEnableCtrlAltDel(BOOL enable) {
    JY_LOGD("tdmaster", "EnableCtrlAltDel(%d) passthrough", enable);
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
    JY_LOGI("tdmaster", "LibTDMaster input hooks installed");
}
