#pragma once
#include <windows.h>

// Hook kernel32!DeviceIoControl to block calls to the TDNetFilter.sys
// kernel driver (web-filtering rules are delivered via IOCTLs
// 0x120004 ~ 0x120018; blocking them keeps the driver in "no filter"
// mode so 80/443 traffic is NOT redirected to the local block page).
void InstallNetFilterGuard();

// Runtime enable/disable (hot-update, FEATURE_NETSIM)
void SetNetFilterEnabled(bool enable);

// Actively clear already-configured filtering (teacher may have pushed
// whitelist/blacklist rules before this tool was injected):
// sends mode=0 + clears both rule lists on the driver.
void ClearNetFilter();
