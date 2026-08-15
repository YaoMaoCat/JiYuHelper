#include "applist.hpp"
#include "engine.hpp"
#include "../core/log.hpp"

// Allow our own code to temporarily disable app filtering
// (e.g., the monitor thread needs unfiltered EnumWindows)
static bool g_filterEnabled = true;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetAppListEnabled(bool enable) {
    g_enabled = enable;
    
}

void EnableAppFilter(bool enable) {
    g_filterEnabled = enable;
}

typedef BOOL(WINAPI* EnumWindows_t)(WNDENUMPROC, LPARAM);
static EnumWindows_t Real_EnumWindows = nullptr;

struct FilterCtx { WNDENUMPROC origFunc; LPARAM origParam; };

static BOOL WINAPI ProxyEnumWindows(WNDENUMPROC lpEnumFunc, LPARAM lParam) {
    if (!g_enabled) {
        return Real_EnumWindows(lpEnumFunc, lParam);
    }
    if (!g_filterEnabled) {
        return Real_EnumWindows(lpEnumFunc, lParam);
    }

    FilterCtx ctx = { lpEnumFunc, lParam };
    Real_EnumWindows([](HWND hwnd, LPARAM lp) -> BOOL {
        auto& fctx = *(FilterCtx*)lp;

        if (!IsWindowVisible(hwnd)) {
            return fctx.origFunc(hwnd, fctx.origParam);
        }

        wchar_t title[128], cls[128];
        int tlen = GetWindowTextW(hwnd, title, 128);
        GetClassNameW(hwnd, cls, 128);

        // Allow system windows
        if (wcscmp(cls, L"Shell_TrayWnd") == 0 ||
            wcscmp(cls, L"DV2ControlHost") == 0 ||
            wcscmp(cls, L"Windows.UI.Composition.DesktopWindowContentBridge") == 0) {
            return fctx.origFunc(hwnd, fctx.origParam);
        }

        // Hide windows with caption + non-empty title (user apps)
        LONG style = GetWindowLongW(hwnd, GWL_STYLE);
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        if ((style & WS_EX_APPWINDOW) || (hasCaption && tlen > 0)) {
            return TRUE; // skip
        }

        return fctx.origFunc(hwnd, fctx.origParam);
    }, (LPARAM)&ctx);

    return TRUE;
}

void InstallAppListHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"user32", nullptr, "EnumWindows", ProxyEnumWindows, (void**)&Real_EnumWindows, "applist" },
    };
    InstallHooks(hooks);
}
