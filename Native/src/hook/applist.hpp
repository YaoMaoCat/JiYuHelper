#pragma once
// Hook EnumWindows to hide real applications from teacher
void InstallAppListHook();

// Temporarily enable/disable the filter (for monitor thread etc.)
void EnableAppFilter(bool enable);

// Runtime enable/disable (hot-update)
void SetAppListEnabled(bool enable);
