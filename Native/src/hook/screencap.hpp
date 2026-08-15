#pragma once
#include <windows.h>

// Hook GDI BitBlt/StretchBlt to return fake screen data for detailed monitoring.
// JiYu's CDesktopCapture uses H.264 encoding; the source pixel data
// likely comes from BitBlt or IDXGIOutputDuplication.
// We hook BitBlt/StretchBlt as a first line of defense.
void InstallScreenCapHook();

// Runtime enable/disable (hot-update)
void SetScreenCapEnabled(bool enable);
