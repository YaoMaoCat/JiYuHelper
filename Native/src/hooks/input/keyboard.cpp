#include "keyboard.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include <vector>

// Null hook: returns 0 so the system processes the event normally.
// For LL hooks, 0 = process event, 1 = block event.
static LRESULT CALLBACK NullHook(int code, WPARAM w, LPARAM l) {
    if (code >= 0) return 0; // allow all input through
    return CallNextHookEx(nullptr, code, w, l);
}

typedef HHOOK(WINAPI* SHW_t)(int, HOOKPROC, HINSTANCE, DWORD);
static SHW_t Real_SHW = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetKeyboardEnabled(bool enable) {
    g_enabled = enable;
}

static HHOOK WINAPI ProxySHW(int id, HOOKPROC lpfn, HINSTANCE hMod, DWORD tid) {
    if (!g_enabled) return Real_SHW(id, lpfn, hMod, tid);
    if (id == WH_KEYBOARD_LL || id == WH_MOUSE_LL) {
        JY_LOGT(3000, "keyboard", "WH_KEYBOARD_LL/WH_MOUSE_LL registration replaced by null hook");
        return Real_SHW(id, NullHook, GetModuleHandleW(nullptr), tid);
    }
    return Real_SHW(id, lpfn, hMod, tid);
}

typedef HHOOK(WINAPI* SHA_t)(int, HOOKPROC, HINSTANCE, DWORD);
static SHA_t Real_SHA = nullptr;

static HHOOK WINAPI ProxySHA(int id, HOOKPROC lpfn, HINSTANCE hMod, DWORD tid) {
    if (!g_enabled) return Real_SHA(id, lpfn, hMod, tid);
    if (id == WH_KEYBOARD_LL || id == WH_MOUSE_LL) {
        JY_LOGT(3000, "keyboard", "WH_KEYBOARD_LL/WH_MOUSE_LL registration replaced by null hook");
        return Real_SHA(id, NullHook, GetModuleHandleW(nullptr), tid);
    }
    return Real_SHA(id, lpfn, hMod, tid);
}

void InstallKeyboardHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"user32", nullptr, "SetWindowsHookExW", ProxySHW, (void**)&Real_SHW, "keyboard W" },
        { HookType::MinHook, L"user32", nullptr, "SetWindowsHookExA", ProxySHA, (void**)&Real_SHA, "keyboard A" },
    };
    InstallHooks(hooks);
}
