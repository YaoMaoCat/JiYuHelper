#pragma once
#include <windows.h>

// Hook SetForegroundWindow/BringWindowToTop to block JiYu from
// stealing focus back to the black screen / broadcast window.
void InstallFocusLockHook();

// Runtime enable/disable (hot-update)
void SetFocusLockEnabled(bool enable);
