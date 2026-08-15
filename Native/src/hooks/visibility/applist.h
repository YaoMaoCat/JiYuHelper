#pragma once
#include <windows.h>

// ============================================================================
// Application list hiding (FEATURE_APPLIST).
//
// Hooks EnumWindows and hides visible windows that look like user
// applications (WS_EX_APPWINDOW or caption + non-empty title), so the
// teacher's application list shows an empty desktop.
// ============================================================================

void InstallAppListHook();

// Temporarily disable the filter (e.g. the monitor thread needs
// unfiltered EnumWindows to detect the black screen).
void EnableAppFilter(bool enable);

// Runtime enable/disable (hot-update)
void SetAppListEnabled(bool enable);
