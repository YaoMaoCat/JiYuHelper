#pragma once
#include <windows.h>

// ============================================================================
// Screen capture shielding (FEATURE_SCREENCAP).
//
// Hooks GDI BitBlt and returns fake screen data (screen.png, tiled) for
// large captures, so the teacher's detailed monitoring (H.264 capture via
// LibTDDesk2.dll) sees a fake image. Small areas (< 100x100) are UI
// rendering and pass through.
// ============================================================================

void InstallScreenCapHook();

// Runtime enable/disable (hot-update)
void SetScreenCapEnabled(bool enable);
