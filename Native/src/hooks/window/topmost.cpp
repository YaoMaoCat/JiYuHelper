#include "topmost.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include <vector>

typedef HWND(WINAPI* CWE_t)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
static CWE_t Real_CWE = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetTopmostEnabled(bool enable) {
    g_enabled = enable;
}

static HWND WINAPI ProxyCWE(DWORD e, LPCWSTR c, LPCWSTR t, DWORD s,
    int x, int y, int w, int h, HWND p, HMENU m, HINSTANCE i, LPVOID l)
{
    if (!g_enabled) return Real_CWE(e, c, t, s, x, y, w, h, p, m, i, l);
    if ((e & WS_EX_TOPMOST) && (s & WS_POPUP) && !(s & WS_CAPTION)) {
        JY_LOGI("topmost", "stripped WS_EX_TOPMOST on class: %ws", c);
        e &= ~WS_EX_TOPMOST;
    }
    return Real_CWE(e, c, t, s, x, y, w, h, p, m, i, l);
}

void InstallTopmostHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"user32", nullptr, "CreateWindowExW", ProxyCWE, (void**)&Real_CWE, "topmost" },
    };
    InstallHooks(hooks);
}
