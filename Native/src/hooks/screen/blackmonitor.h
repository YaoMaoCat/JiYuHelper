#pragma once
#include <windows.h>

// ============================================================================
// Black screen monitor (FEATURE_BLACKMON).
//
// Background thread that scans the student process's windows and converts
// detected black-screen / broadcast windows into normal windowed windows
// (adds caption + system menu, makes them draggable, drops topmost), so
// the classroom screen is not actually locked.
// ============================================================================

// Start background monitor thread. Call once after hooks are installed.
void StartMonitor();

// Runtime enable/disable (hot-update)
void SetMonitorEnabled(bool enable);

// Stop the monitor thread (call from BypassShutdown before unload)
void StopMonitor();
