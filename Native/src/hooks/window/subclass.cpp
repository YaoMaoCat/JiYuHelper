#include "subclass.h"
#include "../../common/log/log.h"
#include <windowsx.h>

static LRESULT CALLBACK SubclassProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    WNDPROC orig = (WNDPROC)GetPropW(hwnd, L"Orig");
    if (!orig) return DefWindowProcW(hwnd, msg, wp, lp);

    if (msg == WM_NCHITTEST) {
        LRESULT r = CallWindowProcW(orig, hwnd, msg, wp, lp);
        if (r == HTCLIENT) {
            POINT pt = { GET_X_LPARAM(lp), GET_Y_LPARAM(lp) };
            ScreenToClient(hwnd, &pt);
            if (pt.y < 30) return HTCAPTION;  // drag by top 30px
        }
        return r;
    }
    if (msg == WM_NCLBUTTONDOWN && wp == HTCLOSE) {
        DestroyWindow(hwnd);
        return 0;
    }
    if (msg == WM_CLOSE) {
        DestroyWindow(hwnd);
        return 0;
    }
    return CallWindowProcW(orig, hwnd, msg, wp, lp);
}

void MakeWindowDraggable(HWND hwnd) {
    WNDPROC orig = (WNDPROC)GetWindowLongPtrW(hwnd, GWLP_WNDPROC);
    SetPropW(hwnd, L"Orig", (HANDLE)orig);
    SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)SubclassProc);

    // re-enable close button in system menu
    HMENU hm = GetSystemMenu(hwnd, FALSE);
    if (hm) EnableMenuItem(hm, SC_CLOSE, MF_BYCOMMAND | MF_ENABLED);

    // force non-client area redraw
    SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    JY_LOGD("subclass", "subclassed 0x%p for drag/close", hwnd);
}
