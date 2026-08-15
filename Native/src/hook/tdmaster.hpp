#pragma once
#include <windows.h>

// Hook LibTDMaster.dll exports that control local input locking.
// Real mechanism (from PE analysis):
//   LibTDMaster!LockLocalInput       // Locks ALL local keyboard/mouse
//   LibTDMaster!UnLockLocalInput     // Unlocks local input
//   LibTDMaster!HookLocalInputToRemoteHost  // Redirects input to teacher
//   LibTDMaster!EnableCtrlAltDel     // Controls Ctrl+Alt+Del behavior
void InstallTDMasterHook();

// Runtime enable/disable (hot-update)
void SetTDMasterEnabled(bool enable);
