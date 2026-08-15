#pragma once
#include <windows.h>

// Hook process creation/termination and shutdown APIs to prevent
// JiYu from executing teacher commands (open app, close app, shutdown).
//
// From DLL analysis, StudentMain.exe uses:
//   CreateProcessW / CreateProcessAsUserW / WinExec  → process creation
//   TerminateProcess                                 → process killing
//   ShellExecuteW / ShellExecuteExW                  → shell execution
//   Shutdown.exe (separate process) calls ExitWindowsEx → shutdown
void InstallProcGuardHook();

// Runtime enable/disable (hot-update)
void SetProcGuardEnabled(bool enable);
