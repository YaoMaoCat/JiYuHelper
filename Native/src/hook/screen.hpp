#pragma once
// Hook EncodeToJPEGBuffer to replace captured screen with screen.png
void InstallScreenFakeHook();

// Reload the fake screen image from screen.png (app replaced the file)
void ReloadScreenFake();

// Release fake-screen resources + GDI+ (unlock screen.png) before unload
void ReleaseScreenFake();

// Runtime enable/disable (hot-update)
void SetScreenFakeEnabled(bool enable);
