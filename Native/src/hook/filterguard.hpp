#pragma once
#include <windows.h>

// Hook minifilter communication APIs to prevent JiYu from
// blocking USB, CD, and file execution on student computer.
//
// From PE analysis:
//   LibTDFileFilter.dll + LibTDUsbHook10.dll
//     → use FilterConnectCommunicationPort + FilterSendMessage
//       to talk to TDFileFilter.sys (kernel minifilter driver)
//
//   libTDMaster.dll
//     → uses DeviceIoControl + CreateFile to talk to drivers
//
// TDNetFilter.sys uses kernel WFP directly - can't hook from user mode.
void InstallFilterGuardHook();

// Runtime enable/disable (hot-update)
void SetFilterGuardEnabled(bool enable);
