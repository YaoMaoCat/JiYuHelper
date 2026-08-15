#pragma once
#include <windows.h>

// ============================================================================
// Topmost stripping (FEATURE_TOPMOST).
//
// Intercepts CreateWindowExW and removes WS_EX_TOPMOST from borderless
// popup windows (the teacher's black screen / broadcast windows).
// ============================================================================

void InstallTopmostHook();

// Runtime enable/disable (hot-update)
void SetTopmostEnabled(bool enable);
