#include "config.hpp"
#include <cstdio>
#include <cstring>
#include <cwchar>

// ============================================================================
// Config is loaded from "hook.cfg" next to the DLL (written by JiYuHelper app).
//
// Format: one "key=value" per line, '#' starts a comment, bool = 0/1.
// Unknown/missing keys keep the default (false = feature disabled).
//
// Keys (must match the toggles on the app's "控制" page):
//   enableKeyboardBypass   键盘钩子绕过 (hook/keyboard.cpp)
//   enableTopmostBlock     置顶窗口剥离 + 焦点锁定 (topmost/focuslock)
//   enableAppListBlock     应用列表屏蔽 (applist.cpp)
//   enableProcListBlock    进程列表屏蔽 + 进程操作守护 (proclist/procguard/prochookguard)
//   enableScreenFake       屏幕假屏 + 捕获屏蔽 (screen/screencap/dispcap)
//   enableRemoteBlock      远程输入拦截 + 输入锁定放行 + 设备过滤屏蔽 (remote/tdmaster/filterguard)
//   enableBlackMonitor     黑屏监控 (monitor.cpp)
//   enableNetSimBlock      网络仿真屏蔽 (bypass_master: BeginSimulate/StopSimulate/DeviceIoControl)
//
// If hook.cfg is missing, ALL features stay disabled (safe default).
// ============================================================================

// ============================================================================
// 注意: 本模块编译进 DLL (bypass_main.dll / bypass_master.dll),
// GetModuleHandleW(nullptr) 返回的是宿主进程主模块路径, 不是 DLL 路径!
// 必须用 GetModuleHandleExW(FROM_ADDRESS) 以本代码地址反查自身模块,
// 否则 hook.cfg / screen.png 会到宿主 (StudentMain.exe) 目录寻找。
// ============================================================================

static void GetOwnModulePath(wchar_t* buf, size_t len) {
    HMODULE self = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPCWSTR)(LPVOID)&GetConfig, &self);
    GetModuleFileNameW(self ? self : GetModuleHandleW(nullptr), buf, (DWORD)len);
}

Config& GetConfig() {
    static Config cfg = {};
    static bool loaded = false;
    if (!loaded) {
        loaded = true;
        cfg.enableKeyboardBypass = false;
        cfg.enableTopmostBlock   = false;
        cfg.enableAppListBlock   = false;
        cfg.enableProcListBlock  = false;
        cfg.enableScreenFake     = false;
        cfg.enableRemoteBlock    = false;
        cfg.enableBlackMonitor   = false;
        cfg.enableNetSimBlock    = false;

        // Fake screen image: screen.png next to THIS DLL
        GetOwnModulePath(cfg.screenPngPath, MAX_PATH);
        wchar_t* sep = wcsrchr(cfg.screenPngPath, L'\\');
        if (sep) wcscpy(sep + 1, L"screen.png");
    }
    return cfg;
}

static bool ReadBool(const char* value, bool* out) {
    if (!value) return false;
    if (strcmp(value, "1") == 0 || _stricmp(value, "true") == 0)  { *out = true;  return true; }
    if (strcmp(value, "0") == 0 || _stricmp(value, "false") == 0) { *out = false; return true; }
    return false;
}

static void Trim(char* s) {
    char* p = s;
    while (*p == ' ' || *p == '\t') p++;
    if (p != s) memmove(s, p, strlen(p) + 1);
    size_t n = strlen(s);
    while (n > 0 && (s[n-1] == ' ' || s[n-1] == '\t' || s[n-1] == '\r' || s[n-1] == '\n'))
        s[--n] = 0;
}

void LoadConfig() {
    Config& cfg = GetConfig();

    wchar_t cfgPath[MAX_PATH];
    GetOwnModulePath(cfgPath, MAX_PATH);
    wchar_t* sep = wcsrchr(cfgPath, L'\\');
    if (sep) wcscpy(sep + 1, L"hook.cfg");

    FILE* f = _wfopen(cfgPath, L"r");
    if (!f) return;

    char line[512];
    while (fgets(line, sizeof(line), f)) {
        char* p = line;
        while (*p == ' ' || *p == '\t') p++;
        if (*p == '#' || *p == '\n' || *p == '\r' || *p == 0) continue;

        char* eq = strchr(p, '=');
        if (!eq) continue;
        *eq = 0;
        Trim(p);
        char* val = eq + 1;
        Trim(val);

        bool b = false;
        if (!ReadBool(val, &b)) continue;

        if      (strcmp(p, "enableKeyboardBypass") == 0) cfg.enableKeyboardBypass = b;
        else if (strcmp(p, "enableTopmostBlock")   == 0) cfg.enableTopmostBlock   = b;
        else if (strcmp(p, "enableAppListBlock")   == 0) cfg.enableAppListBlock   = b;
        else if (strcmp(p, "enableProcListBlock")  == 0) cfg.enableProcListBlock  = b;
        else if (strcmp(p, "enableScreenFake")     == 0) cfg.enableScreenFake     = b;
        else if (strcmp(p, "enableRemoteBlock")    == 0) cfg.enableRemoteBlock    = b;
        else if (strcmp(p, "enableBlackMonitor")   == 0) cfg.enableBlackMonitor   = b;
        else if (strcmp(p, "enableNetSimBlock")    == 0) cfg.enableNetSimBlock    = b;
    }
    fclose(f);
}
