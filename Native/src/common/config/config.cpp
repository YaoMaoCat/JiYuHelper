#include "config.h"
#include "../log/log.h"
#include <cstdio>
#include <cstring>
#include <cwchar>

// ============================================================================
// hook.cfg keys (must match the toggles on the app's "控制" page):
//   enableKeyboardBypass   键盘钩子绕过
//   enableTopmostBlock     置顶窗口剥离 + 焦点锁定
//   enableAppListBlock     应用列表屏蔽
//   enableProcListBlock    进程列表屏蔽 + 进程操作守护
//   enableScreenFake       假屏 + 截屏屏蔽
//   enableRemoteBlock      远程输入拦截 + 输入锁定放行 + 设备过滤屏蔽
//   enableBlackMonitor     黑屏监控
//   enableNetSimBlock      网络仿真屏蔽 (MasterHelper)
//
// 注意: 本模块编译进 DLL, GetModuleHandleW(nullptr) 返回宿主主模块路径,
// 不是 DLL 路径! 必须用 GetModuleHandleExW(FROM_ADDRESS) 以本代码地址
// 反查自身模块, 否则 hook.cfg / screen.png 会到宿主目录寻找。
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
        // zero-initialized defaults: all features disabled
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
    if (!f) {
        JY_LOGI("config", "hook.cfg not found (%ls), all features disabled", cfgPath);
        return;
    }

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

    JY_LOGI("config", "hook.cfg loaded: kb=%d top=%d app=%d proc=%d fake=%d remote=%d black=%d netsim=%d",
        (int)cfg.enableKeyboardBypass, (int)cfg.enableTopmostBlock, (int)cfg.enableAppListBlock,
        (int)cfg.enableProcListBlock, (int)cfg.enableScreenFake, (int)cfg.enableRemoteBlock,
        (int)cfg.enableBlackMonitor, (int)cfg.enableNetSimBlock);
}
