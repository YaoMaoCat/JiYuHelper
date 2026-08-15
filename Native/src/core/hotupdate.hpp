#pragma once
#include <windows.h>
#include <cstdint>

// ============================================================================
// Hot-update feature mask.
// Bit 定义与 JiYuHelper App (HookConfigWriter.BuildFeatureMask) 保持一致。
// 通过管道命令 "UPDATE|0x<hex>" 下发, 运行时启用/禁用各 hook 模块。
// ============================================================================

#define FEATURE_KEYBOARD   (1ULL << 0)   // hook/keyboard.cpp        (EnableKeyboardBypass)
#define FEATURE_TOPMOST    (1ULL << 1)   // hook/topmost.cpp         (EnableTopmostStrip)
#define FEATURE_FOCUS      (1ULL << 2)   // hook/focuslock.cpp       (EnableFocusLock)
#define FEATURE_APPLIST    (1ULL << 3)   // hook/applist.cpp         (EnableAppList)
#define FEATURE_PROCLIST   (1ULL << 4)   // hook/proclist.cpp        (EnableProcList)
#define FEATURE_PROCGUARD  (1ULL << 5)   // hook/procguard.cpp       (EnableProcGuard)
#define FEATURE_PROCHOOK   (1ULL << 6)   // master guard: TDProcHookEnableTerminate (EnableProcHookGuard)
#define FEATURE_SCREENFAKE (1ULL << 7)   // hook/screen.cpp          (EnableScreenFake)
#define FEATURE_SCREENCAP  (1ULL << 8)   // hook/screencap.cpp       (EnableScreenCap)
#define FEATURE_BLACKMON   (1ULL << 9)   // monitor/monitor.cpp      (EnableBlackMonitor)
#define FEATURE_REMOTE     (1ULL << 10)  // hook/remote.cpp          (EnableRemoteInput)
#define FEATURE_INPUTLOCK  (1ULL << 11)  // hook/tdmaster.cpp        (EnableInputLock)
#define FEATURE_FILTER     (1ULL << 12)  // hook/filterguard.cpp     (EnableFilterGuard)
#define FEATURE_NETSIM     (1ULL << 13)  // master guard: BeginSimulate/DeviceIoControl (EnableNetSimBlock)

// Apply a feature mask: enable/disable each installed hook module at runtime.
// Unused bits are ignored. Must be called AFTER hooks are installed.
void HotUpdate(uint64_t mask);
