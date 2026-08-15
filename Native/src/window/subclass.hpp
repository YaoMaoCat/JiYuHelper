#pragma once
#include <windows.h>

// Subclass a window so its title bar is draggable and X button works.
// Call this after adding WS_CAPTION / WS_SYSMENU to a window.
void MakeWindowDraggable(HWND hwnd);
