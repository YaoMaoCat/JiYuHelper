#pragma once
#include <windows.h>

// ============================================================================
// Process list hiding (FEATURE_PROCLIST).
//
// Hooks Process32FirstW/NextW AND EnumProcesses (StudentMain uses both
// toolhelp and psapi). WHITELIST mode: only system-essential processes
// remain visible; user applications are hidden from the teacher view.
// ============================================================================

void InstallProcListHook();

// Runtime enable/disable (hot-update)
void SetProcListEnabled(bool enable);
