#pragma once
#include <windows.h>

// ============================================================================
// Fake screen (FEATURE_SCREENFAKE).
//
// Hooks LibJPEG20!EncodeToJPEGBuffer and overwrites the source pixel buffer
// with a rescaled copy of screen.png before the real encoder runs, so the
// teacher's screen monitor receives a fake image.
//
// EncodeToJPEGBuffer signature (7 params, from log analysis):
//   (input, w, h, stride, output, outSize, quality) -> int
// ============================================================================

void InstallScreenFakeHook();

// Reload the fake screen image from screen.png (called on "SCREEN_RELOAD"
// pipe command after the app replaced the file).
void ReloadScreenFake();

// Release fake-screen resources + GDI+ (unlock screen.png) before unload.
void ReleaseScreenFake();

// Runtime enable/disable (hot-update)
void SetScreenFakeEnabled(bool enable);
