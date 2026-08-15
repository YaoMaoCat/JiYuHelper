#pragma once
#include <windows.h>
#include <cstdint>

// ============================================================================
// MasterHelper.exe proc guard (injected via jyhelper_master.dll).
//
// Blocks process termination/spawning and network simulation from the
// teacher side:
//   TerminateProcess, CreateProcessW        (FEATURE_PROCGUARD)
//   TDProcHookEnableTerminate               (FEATURE_PROCHOOK)
//   BeginSimulate / StopSimulate / DIO      (FEATURE_NETSIM)
// ============================================================================

// Install all MasterHelper.exe hooks (unconditional; runtime state comes
// from MasterHotUpdate, initially driven by hook.cfg).
void InstallMasterProcGuard();

// Runtime enable/disable (hot-update). Bits:
//   FEATURE_PROCGUARD -> TerminateProcess/CreateProcessW
//   FEATURE_PROCHOOK  -> TDProcHookEnableTerminate
//   FEATURE_NETSIM    -> BeginSimulate/StopSimulate/DeviceIoControl
void MasterHotUpdate(uint64_t mask);
