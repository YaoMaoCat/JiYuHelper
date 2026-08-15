#pragma once
#include <windows.h>

// ============================================================================
// Runtime configuration, loaded once from "hook.cfg" next to the DLL
// (written by the JiYuHelper app). Safe default: all features disabled.
// ============================================================================

struct Config {
    wchar_t screenPngPath[MAX_PATH]; // fake screen image: screen.png next to THIS DLL
    bool    enableKeyboardBypass;    // 键盘钩子绕过
    bool    enableTopmostBlock;      // 置顶窗口剥离 + 焦点锁定
    bool    enableAppListBlock;      // 应用列表屏蔽
    bool    enableProcListBlock;     // 进程列表屏蔽 + 进程操作守护
    bool    enableScreenFake;        // 假屏 + 截屏屏蔽
    bool    enableRemoteBlock;       // 远程输入拦截 + 输入锁定放行 + 设备过滤屏蔽
    bool    enableBlackMonitor;      // 黑屏监控
    bool    enableNetSimBlock;       // 网络仿真屏蔽 (MasterHelper: BeginSimulate/StopSimulate/DeviceIoControl)
};

// First call initializes defaults and resolves screen.png path; returns the singleton.
Config& GetConfig();

// Parse hook.cfg (key=value lines, '#' comments, bool 0/1/true/false).
// Missing file or keys keep defaults. Idempotent; call once after DLL load.
void LoadConfig();
