#pragma once
#include <windows.h>

// Hook CreateWindowExW to strip WS_EX_TOPMOST from black screen windows
void InstallTopmostHook();

// Runtime enable/disable (hot-update)
void SetTopmostEnabled(bool enable);
