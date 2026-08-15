#pragma once
#include <windows.h>

// ============================================================================
// TDMaster input lock (FEATURE_INPUTLOCK).
//
// Hooks LibTDMaster.dll exports that control local input locking:
//   LockLocalInput                 -> locks ALL local keyboard/mouse
//   UnLockLocalInput               -> unlocks local input
//   HookLocalInputToRemoteHost     -> redirects input to the teacher
//   EnableCtrlAltDel               -> controls Ctrl+Alt+Del behavior
//
// IMPORTANT: LockLocalInput sets internal state that UnLockLocalInput
// depends on. If we no-op LockLocalInput, UnLockLocalInput hangs/crashes.
// Solution: let LockLocalInput run (its hook-based input blocking is
// already nullified by the keyboard hook), but block the higher-level
// "hook to remote" function so input stays local.
// ============================================================================

void InstallTDMasterHook();

// Runtime enable/disable (hot-update)
void SetTDMasterEnabled(bool enable);
