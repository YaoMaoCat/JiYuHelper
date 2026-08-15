#pragma once
#include <windows.h>
#include <set>

// Start background monitor thread that auto-windows black screens.
// Call once after hooks are installed.
void StartMonitor();

// Runtime enable/disable (hot-update)
void SetMonitorEnabled(bool enable);

// Stop the monitor thread (call from BypassShutdown before unload)
void StopMonitor();
