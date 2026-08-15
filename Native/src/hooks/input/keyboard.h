#pragma once
#include <windows.h>

// ============================================================================
// Keyboard/mouse hook bypass (FEATURE_KEYBOARD).
//
// Intercepts SetWindowsHookExW/A and swaps WH_KEYBOARD_LL / WH_MOUSE_LL
// registrations for a null hook so the teacher's global input hooks
// never see real input (and therefore cannot block it).
// ============================================================================

void InstallKeyboardHook();

// Runtime enable/disable (hot-update)
void SetKeyboardEnabled(bool enable);
