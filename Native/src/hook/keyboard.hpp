#pragma once
#include <windows.h>

// Hook SetWindowsHookExW/A to block WH_KEYBOARD_LL registration
void InstallKeyboardHook();

// Runtime enable/disable (hot-update)
void SetKeyboardEnabled(bool enable);
