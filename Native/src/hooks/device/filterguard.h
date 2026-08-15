#pragma once
#include <windows.h>

// ============================================================================
// Filter guard (FEATURE_FILTER).
//
// Blocks minifilter communication so JiYu cannot restrict USB / CD / file
// execution on the student machine:
//   LibTDFileFilter.dll + LibTDUsbHook10.dll
//     -> fltlib.FilterConnectCommunicationPort("\\.\TDFileFilterPort")
//     -> fltlib.FilterSendMessage(...)
//     -> TDFileFilter.sys (kernel minifilter) enforces the block
// Intercepting the port connect prevents ANY communication with the driver.
// ============================================================================

void InstallFilterGuardHook();

// Runtime enable/disable (hot-update)
void SetFilterGuardEnabled(bool enable);
