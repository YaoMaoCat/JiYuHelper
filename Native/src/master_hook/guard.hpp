#pragma once
#include <windows.h>
#include "../core/hotupdate.hpp"

// Install all MasterHelper.exe hooks:
//   TerminateProcess, CreateProcessW, TDProcHookEnableTerminate,
//   BeginSimulate, StopSimulate, DeviceIoControl
void InstallMasterProcGuard();

// Runtime enable/disable (hot-update). Bits:
//   FEATURE_PROCGUARD -> TerminateProcess/CreateProcessW
//   FEATURE_PROCHOOK  -> TDProcHookEnableTerminate
//   FEATURE_NETSIM    -> BeginSimulate/StopSimulate/DeviceIoControl
void MasterHotUpdate(uint64_t mask);
