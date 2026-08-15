#pragma once
#include <windows.h>

// ============================================================================
// Focus lock (FEATURE_FOCUS).
//
// Blocks focus-stealing APIs (SetForegroundWindow / BringWindowToTop /
// SetActiveWindow / SetWindowPos) when the target window belongs to the
// student process, so JiYu cannot pull focus back to its black screen.
// SetWindowPos is only redirected for black-screen windows ("Afx:*:20b:*"),
// broadcast windows and child controls pass through.
// ============================================================================

void InstallFocusLockHook();

// Runtime enable/disable (hot-update)
void SetFocusLockEnabled(bool enable);
