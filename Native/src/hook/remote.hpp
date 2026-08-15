#pragma once
// Hook SendInput to block teacher's remote mouse/keyboard control
void InstallRemoteInputHook();

// Runtime enable/disable (hot-update)
void SetRemoteEnabled(bool enable);
