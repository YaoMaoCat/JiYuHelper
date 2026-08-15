#pragma once
#include <windows.h>

// Hook process enumeration to hide processes from teacher view.
// WHITELIST mode: only system-essential processes are visible.
// All user applications (browsers, games, IM, IDEs, terminals) are hidden.
void InstallProcListHook();

// Runtime enable/disable (hot-update)
void SetProcListEnabled(bool enable);
