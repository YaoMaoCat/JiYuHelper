#include "focuslock.hpp"
#include "engine.hpp"
#include "../core/log.hpp"

// Hook focus-stealing APIs used by JiYu.
// SetWindowPos is the primary mechanism for broadcast focus stealing.

typedef BOOL (WINAPI* SFW_t)(HWND);
static SFW_t Real_SetForegroundWindow = nullptr;

typedef BOOL (WINAPI* BTT_t)(HWND);
static BTT_t Real_BringWindowToTop = nullptr;

typedef HWND (WINAPI* SAW_t)(HWND);
static SAW_t Real_SetActiveWindow = nullptr;

typedef BOOL (WINAPI* SWP_t)(HWND, HWND, int, int, int, int, UINT);
static SWP_t Real_SetWindowPos = nullptr;

static bool IsOurProcess(HWND hwnd) {
    DWORD pid;
    GetWindowThreadProcessId(hwnd, &pid);
    return (pid == GetCurrentProcessId());
}

// Only block SetWindowPos for windows with :20b: class (black screen).
// NOT for broadcast windows, child controls, or main window.
static bool IsBlackScreenForSWP(HWND hwnd) {
    wchar_t cls[64] = {0};
    GetClassNameW(hwnd, cls, 64);
    if (wcsncmp(cls, L"Afx:", 4) != 0) return false;
    if (wcscmp(cls, L"Afx:00400000:b") == 0) return false;
    return (wcsstr(cls, L":20b:") != nullptr);
}

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetFocusLockEnabled(bool enable) {
    g_enabled = enable;
    
}

// ---- SetForegroundWindow ----
static BOOL WINAPI ProxySetForegroundWindow(HWND hwnd) {
    if (!g_enabled) return Real_SetForegroundWindow(hwnd);
    if (IsOurProcess(hwnd)) {
        LogThrottled(3000, "[Focus] SFW blocked: hwnd=0x%p", hwnd);
        return TRUE;
    }
    return Real_SetForegroundWindow(hwnd);
}

// ---- BringWindowToTop ----
static BOOL WINAPI ProxyBringWindowToTop(HWND hwnd) {
    if (!g_enabled) return Real_BringWindowToTop(hwnd);
    if (IsOurProcess(hwnd)) {
        LogThrottled(3000, "[Focus] BTT blocked: hwnd=0x%p", hwnd);
        return TRUE;
    }
    return Real_BringWindowToTop(hwnd);
}

// ---- SetActiveWindow ----
static HWND WINAPI ProxySetActiveWindow(HWND hwnd) {
    if (!g_enabled) return Real_SetActiveWindow(hwnd);
    if (IsOurProcess(hwnd)) {
        LogThrottled(3000, "[Focus] SAW blocked: hwnd=0x%p", hwnd);
        return hwnd;
    }
    return Real_SetActiveWindow(hwnd);
}

// ---- SetWindowPos ----
// Only block HWND_TOP/TOPMOST for :20b: black screen windows.
// Broadcast windows (:b:) and child controls pass through.
static BOOL WINAPI ProxySetWindowPos(HWND hwnd, HWND insertAfter,
    int x, int y, int cx, int cy, UINT flags)
{
    if (!g_enabled) return Real_SetWindowPos(hwnd, insertAfter, x, y, cx, cy, flags);
    if (IsOurProcess(hwnd) && (insertAfter == HWND_TOP || insertAfter == HWND_TOPMOST)) {
        if (IsBlackScreenForSWP(hwnd)) {
            LogThrottled(3000, "[Focus] SWP blocked (topmost -> notopmost)");
            return Real_SetWindowPos(hwnd, HWND_NOTOPMOST, x, y, cx, cy, flags);
        }
        // Broadcast and other windows pass through (no log to avoid spam)
    }
    return Real_SetWindowPos(hwnd, insertAfter, x, y, cx, cy, flags);
}

void InstallFocusLockHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"user32", nullptr, "SetWindowPos",
          ProxySetWindowPos, (void**)&Real_SetWindowPos, "focus SWP" },
        { HookType::MinHook, L"user32", nullptr, "SetForegroundWindow",
          ProxySetForegroundWindow, (void**)&Real_SetForegroundWindow, "focus SFW" },
        { HookType::MinHook, L"user32", nullptr, "BringWindowToTop",
          ProxyBringWindowToTop, (void**)&Real_BringWindowToTop, "focus BTT" },
        { HookType::MinHook, L"user32", nullptr, "SetActiveWindow",
          ProxySetActiveWindow, (void**)&Real_SetActiveWindow, "focus SAW" },
    };
    InstallHooks(hooks);
    Log("[Focus] Focus lock hooks installed (SWP+SFW+BTT+SAW)");
}
