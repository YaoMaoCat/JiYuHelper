#pragma once
#include <windows.h>

// ============================================================================
// Window subclassing utility.
//
// Subclasses a window so its title bar is draggable (top 30px) and the
// X button / WM_CLOSE actually destroys the window. Call after adding
// WS_CAPTION / WS_SYSMENU to a window.
// ============================================================================

void MakeWindowDraggable(HWND hwnd);
