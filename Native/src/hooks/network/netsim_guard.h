#pragma once
#include <windows.h>

// ============================================================================
// Net filter guard (FEATURE_NETSIM, StudentMain side).
//
// Blocks kernel32!DeviceIoControl calls targeting TDNetFilter.sys
// (web-filtering rules are delivered via IOCTLs 0x120004 ~ 0x120018;
// blocking the enable codes keeps the driver in "no filter" mode so
// 80/443 traffic is NOT redirected to the local block page).
//
// Active clearing of already-pushed rules is done by the MasterHelper
// (SYSTEM) via common/util/netfilter.h -- StudentMain cannot open the
// driver device (access denied).
// ============================================================================

void InstallNetFilterGuard();

// Runtime enable/disable (hot-update)
void SetNetFilterEnabled(bool enable);
