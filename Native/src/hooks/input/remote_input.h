#pragma once
#include <windows.h>

// ============================================================================
// Remote input interception (FEATURE_REMOTE).
//
// Hooks user32!SendInput to swallow the teacher's simulated mouse/keyboard
// events (remote control), and user32!BlockInput so JiYu cannot disable
// ALL local input during monitoring.
// ============================================================================

void InstallRemoteInputHook();

// Runtime enable/disable (hot-update)
void SetRemoteEnabled(bool enable);
