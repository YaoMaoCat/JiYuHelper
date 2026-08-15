#include "blackmonitor.h"
#include "../../common/log/log.h"
#include "../../common/pipe/pipe.h"
#include "../window/subclass.h"
#include "../visibility/applist.h"

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

    // must NOT be a black screen (black screen = empty title or "BlackScreen Window")
    wchar_t title[128];
    int tlen = GetWindowTextW(hwnd, title, 128);
    if (tlen == 0 || wcscmp(title, L"BlackScreen Window") == 0) return false;

    // "Capture Wnd" is the thumbnail capture helper, not a broadcast window
    if (wcscmp(title, L"Capture Wnd") == 0) return false;

    // log the broadcast title once for debugging
    static bool s_logged = false;
    if (!s_logged) {
        s_logged = true;
        JY_LOGI("blackmon", "broadcast title: \"%ws\" (len=%d)", title, tlen);
    }
    return true;
}

static void WindowIt(HWND hwnd, const wchar_t* cls, const wchar_t* typeName) {
    // check if already windowed (title changed to "JiYu - windowed")
    wchar_t curTitle[128];
    bool alreadyWindowed = (GetWindowTextW(hwnd, curTitle, 128) > 0 &&
                            wcscmp(curTitle, L"JiYu - windowed") == 0);

    if (!alreadyWindowed) {
        RECT rc;
        GetWindowRect(hwnd, &rc);
        int w = rc.right - rc.left, h = rc.bottom - rc.top;
        JY_LOGI("blackmon", "windowing %ws 0x%p cls=%ws [%dx%d]", typeName, hwnd, cls, w, h);

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

        // notify on first windowing
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

    wchar_t cls[128];
    GetClassNameW(hwnd, cls, 128);
    if (wcsncmp(cls, L"Afx:", 4) != 0) return;

    if (IsBlackScreenWindow(hwnd, cls, 128)) {
        WindowIt(hwnd, cls, L"BlackScreen");
    } else if (IsBroadcastWindow(hwnd, cls, 128)) {
        // if this is a child broadcast window, window its parent instead
        HWND target = hwnd;
        if (!isTopLevel) {
            HWND parent = GetParent(hwnd);
            if (parent && pid == GetCurrentProcessId()) {
                target = parent;
                JY_LOGD("blackmon", "broadcast is child, windowing parent 0x%p instead", parent);
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
    JY_LOGI("blackmon", "started (title-based detector)");

    while (!g_shutdown) {
        if (!g_enabled) { Sleep(500); continue; }
        EnableAppFilter(false);
        // step 1: enumerate all top-level windows
        EnumWindows(EnumTopProc, 0);

        // step 2: for each top-level window, also check its children
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
