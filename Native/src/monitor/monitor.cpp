#include "monitor.hpp"
#include "../core/log.hpp"
#include "../core/pipe.hpp"
#include "../window/subclass.hpp"
#include "../hook/applist.hpp"
#include <set>
#include <string>


// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;
static volatile bool g_shutdown = false;

void SetMonitorEnabled(bool enable) {
    g_enabled = enable;
    
}

void StopMonitor() {
    g_shutdown = true;
    Sleep(700); // wait for MonitorThread to leave its 500ms cycle
}

// --- Window matching ---

static bool IsBlackScreenWindow(HWND hwnd, wchar_t* cls, int clsLen) {
    wchar_t title[128];
    int titleLen = GetWindowTextW(hwnd, title, 128);
    bool titleMatch = (wcscmp(title, L"BlackScreen Window") == 0) || (titleLen == 0);
    if (!titleMatch) return false;

    GetClassNameW(hwnd, cls, clsLen);
    if (wcsncmp(cls, L"Afx:", 4) != 0) return false;
    if (wcsstr(cls, L":20b:") == nullptr) return false;
    if (GetWindow(hwnd, GW_OWNER) != nullptr) return false;

    RECT rc;
    GetWindowRect(hwnd, &rc);
    if (rc.left != 0 || rc.top != 0) return false;
    return true;
}

static bool IsBroadcastWindow(HWND hwnd, wchar_t* cls, int clsLen) {
    GetClassNameW(hwnd, cls, clsLen);
    if (wcsncmp(cls, L"Afx:", 4) != 0) return false;
    if (wcsstr(cls, L":20b:") == nullptr) return false;
    if (GetWindow(hwnd, GW_OWNER) != nullptr) return false;

    RECT rc;
    GetWindowRect(hwnd, &rc);
    if (rc.left != 0 || rc.top != 0) return false;

    int sw = GetSystemMetrics(SM_CXSCREEN);
    int sh = GetSystemMetrics(SM_CYSCREEN);
    if (rc.right - rc.left < sw/2 || rc.bottom - rc.top < sh/2) return false;

    // Must NOT be a black screen (black screen = empty title or "BlackScreen Window")
    wchar_t title[128];
    int tlen = GetWindowTextW(hwnd, title, 128);
    if (tlen == 0 || wcscmp(title, L"BlackScreen Window") == 0) return false;

    // "Capture Wnd" is the thumbnail capture helper, not a broadcast window
    if (wcscmp(title, L"Capture Wnd") == 0) return false;

    // Log the broadcast title once for debugging
    static bool s_logged = false;
    if (!s_logged) {
        s_logged = true;
        Log("[Mon] Broadcast title: \"%ws\" (len=%d)", title, tlen);
        // Hex dump raw bytes of title
        char hex[256] = {0};
        for (int i = 0; i < tlen * 2 && i < 60; i++) {
            char tb[8]; sprintf_s(tb, "%02X ", ((BYTE*)title)[i]); strcat_s(hex, tb);
        }
        Log("[Mon]   title hex: %s", hex);
    }
    return true;
}

static void WindowIt(HWND hwnd, const wchar_t* cls, const wchar_t* typeName) {
    // Check if already windowed (title changed to "JiYu - windowed")
    wchar_t curTitle[128];
    bool alreadyWindowed = (GetWindowTextW(hwnd, curTitle, 128) > 0 &&
                            wcscmp(curTitle, L"JiYu - windowed") == 0);

    if (!alreadyWindowed) {
        RECT rc;
        GetWindowRect(hwnd, &rc);
        int w = rc.right - rc.left, h = rc.bottom - rc.top;
        Log("[Mon] Windowing %ws 0x%p cls=%ws [%dx%d]", typeName, hwnd, cls, w, h);

        LONG style = GetWindowLongW(hwnd, GWL_STYLE);
        SetWindowLongW(hwnd, GWL_STYLE,
            style | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_THICKFRAME);
        LONG ex = GetWindowLongW(hwnd, GWL_EXSTYLE);
        SetWindowLongW(hwnd, GWL_EXSTYLE, ex | WS_EX_APPWINDOW);

        int sw = GetSystemMetrics(SM_CXSCREEN);
        int sh = GetSystemMetrics(SM_CYSCREEN);
        SetWindowPos(hwnd, HWND_NOTOPMOST,
                     sw/8, sh/8, sw*3/4, sh*3/4, SWP_FRAMECHANGED);
        SetWindowTextW(hwnd, L"JiYu - windowed");
        MakeWindowDraggable(hwnd);

        // Notify on first windowing
        wchar_t note[256];
        swprintf_s(note, L"%ws windowed", typeName);
        PipeSend("INFO", "unblocked: %ls", note);
    }
}

// --- Per-window check (used by both EnumWindows and EnumChildWindows) ---
static void CheckOneWindow(HWND hwnd, bool isTopLevel) {
    DWORD pid;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != GetCurrentProcessId()) return;

    // Already windowed? skip (handled inside WindowIt)
    wchar_t cls[128];
    GetClassNameW(hwnd, cls, 128);
    if (wcsncmp(cls, L"Afx:", 4) != 0) return;

    if (IsBlackScreenWindow(hwnd, cls, 128)) {
        WindowIt(hwnd, cls, L"BlackScreen");
    } else if (IsBroadcastWindow(hwnd, cls, 128)) {
        // If this is a child broadcast window, window its parent instead
        HWND target = hwnd;
        if (!isTopLevel) {
            HWND parent = GetParent(hwnd);
            if (parent && pid == GetCurrentProcessId()) {
                target = parent;
                Log("[Mon] Broadcast is child, windoing parent 0x%p instead", parent);
            }
        }
        wchar_t tcls[128];
        GetClassNameW(target, tcls, 128);
        WindowIt(target, tcls, L"Broadcast");
    }
}

static BOOL CALLBACK EnumTopProc(HWND hwnd, LPARAM) {
    CheckOneWindow(hwnd, true);
    return TRUE;
}

static BOOL CALLBACK EnumChildProc(HWND hwnd, LPARAM) {
    CheckOneWindow(hwnd, false);
    return TRUE;
}

static DWORD WINAPI MonitorThread(LPVOID) {
    Log("[Mon] Started (title-based detector)");

    while (!g_shutdown) {
        if (!g_enabled) { Sleep(500); continue; }
        EnableAppFilter(false);
        // Step 1: Enumerate all top-level windows
        EnumWindows(EnumTopProc, 0);

        // Step 2: For each top-level window, also check its children
        // (broadcast render window can be a child window)
        EnumWindows([](HWND hwnd, LPARAM) -> BOOL {
            DWORD pid;
            GetWindowThreadProcessId(hwnd, &pid);
            if (pid == GetCurrentProcessId()) {
                EnumChildWindows(hwnd, EnumChildProc, 0);
            }
            return TRUE;
        }, 0);

        EnableAppFilter(true);
        Sleep(500);
    }
    return 0;
}

void StartMonitor() {
    g_shutdown = false;
    HANDLE h = CreateThread(nullptr, 0, MonitorThread, nullptr, 0, nullptr);
    if (h) CloseHandle(h);
}
