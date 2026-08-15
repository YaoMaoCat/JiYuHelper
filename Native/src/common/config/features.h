#pragma once

// ============================================================================
// Hot-update feature mask -- bit layout contract.
//
// Bit 定义与 JiYuHelper App (HookConfigWriter.BuildFeatureMask) 严格一致,
// 通过管道命令 "UPDATE|0x<hex>" 下发, 运行时启用/禁用各 hook 模块。
// 该布局是外部协议的一部分, 不得改动位序/含义。
// ============================================================================

#define FEATURE_KEYBOARD   (1ULL << 0)   // 键盘钩子绕过           (hook/keyboard)
#define FEATURE_TOPMOST    (1ULL << 1)   // 置顶窗口剥离           (hook/topmost)
#define FEATURE_FOCUS      (1ULL << 2)   // 焦点锁定               (hook/focuslock)
#define FEATURE_APPLIST    (1ULL << 3)   // 应用列表屏蔽           (hook/applist)
#define FEATURE_PROCLIST   (1ULL << 4)   // 进程列表屏蔽           (hook/proclist)
#define FEATURE_PROCGUARD  (1ULL << 5)   // 进程操作守护(学生端)   (hook/procguard)
#define FEATURE_PROCHOOK   (1ULL << 6)   // 进程操作守护(教师端)   (master/guard: TDProcHookEnableTerminate)
#define FEATURE_SCREENFAKE (1ULL << 7)   // 假屏                   (hook/screenfake)
#define FEATURE_SCREENCAP  (1ULL << 8)   // 截屏屏蔽               (hook/screencap)
#define FEATURE_BLACKMON   (1ULL << 9)   // 黑屏监控               (hook/blackmonitor)
#define FEATURE_REMOTE     (1ULL << 10)  // 远程输入拦截           (hook/remote)
#define FEATURE_INPUTLOCK  (1ULL << 11)  // 输入锁定放行           (hook/tdmaster)
#define FEATURE_FILTER     (1ULL << 12)  // 设备过滤屏蔽           (hook/filterguard)
#define FEATURE_NETSIM     (1ULL << 13)  // 网络仿真屏蔽(教师端)   (master/guard: BeginSimulate/DeviceIoControl)
