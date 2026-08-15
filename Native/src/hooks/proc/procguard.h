#pragma once
#include <windows.h>

// ============================================================================
// Process guard (FEATURE_PROCGUARD, StudentMain side).
//
// Blocks process creation/termination and shutdown APIs so JiYu cannot
// execute teacher commands (open app, close app, shutdown):
//   CreateProcessW / CreateProcessAsUserW / WinExec  -> process creation
//   TerminateProcess                                 -> process killing
//   ShellExecuteW / ShellExecuteExW                  -> shell execution
//   ExitWindowsEx                                    -> shutdown
// Every block emits a BLOCKED pipe event.
// ============================================================================

void InstallProcGuardHook();

// Runtime enable/disable (hot-update)
void SetProcGuardEnabled(bool enable);
