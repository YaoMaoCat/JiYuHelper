#include "screencap.hpp"
#include "engine.hpp"
#include "../core/log.hpp"
#include "../../config/config.hpp"
#include <gdiplus.h>

using namespace Gdiplus;

// Hook GDI BitBlt used by LibTDDesk2.dll for detailed screen monitoring.
// LibTDDesk2.dll uses: BitBlt, CreateDCW, CreateCompatibleDC, CreateDIBSection, GetDIBits
// The capture is then H.264 encoded with stride = cx*4 (BGR32).

typedef BOOL (WINAPI* BitBlt_t)(HDC, int, int, int, int, HDC, int, int, DWORD);
static BitBlt_t Real_BitBlt = nullptr;

// Cached fake screen image as BGR32 (32bpp, match encoder's expected format)
static BYTE* g_fakeScreen  = nullptr;
static int   g_fakeSW      = 0;
static int   g_fakeSH      = 0;
static bool  g_capLoaded   = false;
static bool  g_inCapture   = false; // re-entrancy guard

static void EnsureFakeScreen() {
    if (g_capLoaded) return;
    g_capLoaded = true;

    Config& cfg = GetConfig();
    Log("[Cap] Loading fake screen from: %ws", cfg.screenPngPath);

    GdiplusStartupInput si;
    ULONG_PTR token;
    GdiplusStartup(&token, &si, nullptr);

    Bitmap* bmp = Bitmap::FromFile(cfg.screenPngPath);
    if (bmp && bmp->GetLastStatus() == Ok) {
        int bw = bmp->GetWidth();
        int bh = bmp->GetHeight();
        g_fakeSW = bw;
        g_fakeSH = bh;

        // Convert to BGR32 (4 bytes per pixel, stride = width * 4)
        g_fakeScreen = (BYTE*)malloc(bw * bh * 4);
        if (g_fakeScreen) {
            BitmapData bd;
            Rect r(0, 0, bw, bh);
            // Use PixelFormat32bppRGB for 32-bit (GDI+ stores as B,G,R,A in memory)
            if (bmp->LockBits(&r, ImageLockModeRead, PixelFormat32bppRGB, &bd) == Ok) {
                BYTE* src = (BYTE*)bd.Scan0;
                for (int y = 0; y < bh; y++) {
                    BYTE* sRow = src + y * bd.Stride;
                    BYTE* dRow = g_fakeScreen + y * bw * 4;
                    for (int x = 0; x < bw; x++) {
                        // GDI+ PixelFormat32bppRGB stores B,G,R,A already
                        // Copy directly as B,G,R,0
                        dRow[x * 4 + 0] = sRow[x * 4 + 0]; // B
                        dRow[x * 4 + 1] = sRow[x * 4 + 1]; // G
                        dRow[x * 4 + 2] = sRow[x * 4 + 2]; // R
                        dRow[x * 4 + 3] = 0;                // reserved/alpha
                    }
                }
                bmp->UnlockBits(&bd);
            }
        }
        delete bmp;
    }

    if (!g_fakeScreen) {
        // Fallback: solid blue
        g_fakeSW = 1920;
        g_fakeSH = 1080;
        g_fakeScreen = (BYTE*)malloc(g_fakeSW * g_fakeSH * 4);
        if (g_fakeScreen) memset(g_fakeScreen, 0x80, g_fakeSW * g_fakeSH * 4);
    }

    GdiplusShutdown(token);
    Log("[Cap] Fake screen ready: %dx%d (BGR32)", g_fakeSW, g_fakeSH);
}

// Check if a BitBlt call is for full/partial screen capture (large area)
// rather than small UI element rendering.
static bool IsScreenCapture(int cx, int cy, HDC hdcDest) {
    // Small areas (< 100x100) are UI element rendering, not screen capture
    if (cx < 100 || cy < 100) return false;

    // Check if source DC is the screen DC
    // Actually we can't easily verify this in a hook.
    // Instead: if it's a large (>50% screen) BitBlt, it's a capture.
    int sw = GetSystemMetrics(SM_CXSCREEN);
    int sh = GetSystemMetrics(SM_CYSCREEN);
    if (cx >= sw/2 || cy >= sh/2) return true;

    // Medium-large captures are likely captures too
    if (cx * cy >= sw * sh / 4) return true;

    return false;
}

// Fill dest HDC with fake screen data
static void FillWithFakeScreen(HDC hdc, int x, int y, int cx, int cy) {
    EnsureFakeScreen();

    // Create a 32-bit DIB section filled with fake screen data
    HDC memDC = CreateCompatibleDC(hdc);
    if (!memDC) return;

    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = cx;
    bmi.bmiHeader.biHeight = -cy; // top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32; // BGR32
    bmi.bmiHeader.biCompression = BI_RGB;

    BYTE* bits = nullptr;
    HBITMAP hBmp = CreateDIBSection(memDC, &bmi, DIB_RGB_COLORS, (void**)&bits, nullptr, 0);
    if (hBmp && bits && g_fakeScreen) {
        // Fill with screen.png data (tiled)
        for (int row = 0; row < cy; row++) {
            int srcY = row % g_fakeSH;
            BYTE* srcRow = g_fakeScreen + srcY * g_fakeSW * 4;
            BYTE* dstRow = bits + row * cx * 4;
            for (int col = 0; col < cx; col++) {
                int srcX = col % g_fakeSW;
                dstRow[col * 4 + 0] = srcRow[srcX * 4 + 0]; // B
                dstRow[col * 4 + 1] = srcRow[srcX * 4 + 1]; // G
                dstRow[col * 4 + 2] = srcRow[srcX * 4 + 2]; // R
                dstRow[col * 4 + 3] = 0;                     // reserved
            }
        }
    }

    if (hBmp) {
        HGDIOBJ old = SelectObject(memDC, hBmp);
        BitBlt(hdc, x, y, cx, cy, memDC, 0, 0, SRCCOPY);
        SelectObject(memDC, old);
        DeleteObject(hBmp);
    }
    DeleteDC(memDC);
}

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetScreenCapEnabled(bool enable) {
    g_enabled = enable;
    
}

// ---- BitBlt hook ----
static BOOL WINAPI ProxyBitBlt(HDC hdc, int x, int y, int cx, int cy,
                                HDC hdcSrc, int x1, int y1, DWORD rop)
{
    // Runtime switch off -> pass through
    if (!g_enabled) return Real_BitBlt(hdc, x, y, cx, cy, hdcSrc, x1, y1, rop);

    LogThrottled(10000, "[Cap] BitBlt screen capture: %dx%d at (%d,%d)", cx, cy, x, y);

    // Check if this looks like a screen capture
    if (!g_inCapture && IsScreenCapture(cx, cy, hdc)) {
        int srcType = GetObjectType(hdcSrc);

        // Only intercept when source DC is a plain display DC (OBJ_DC).
        // Memory DCs (OBJ_MEMDC) are used for rendering whiteboard/video.
        if (srcType == OBJ_DC || srcType == OBJ_MEMDC) {
            // For memory DCs, check if the selected bitmap is a DIB section
            // which would indicate capture target rather than rendering source
            if (srcType == OBJ_MEMDC) {
                HBITMAP hBmp = (HBITMAP)GetCurrentObject(hdcSrc, OBJ_BITMAP);
                if (hBmp) {
                    DIBSECTION ds;
                    if (GetObject(hBmp, sizeof(ds), &ds) && ds.dsBm.bmType == 0) {
                        // DIB section = capture target, not rendering source
                        return Real_BitBlt(hdc, x, y, cx, cy, hdcSrc, x1, y1, rop);
                    }
                }
                // Non-DIB bitmap in memory DC → not screen capture → pass through
                return Real_BitBlt(hdc, x, y, cx, cy, hdcSrc, x1, y1, rop);
            }

            // Source is a display DC (OBJ_DC) → screen capture
            g_inCapture = true;
            static bool logged = false;
            if (!logged) {
                logged = true;
                Log("[Cap] BitBlt screen capture: %dx%d at (%d,%d) rop=0x%08X",
                    cx, cy, x, y, rop);
            }

            FillWithFakeScreen(hdc, x, y, cx, cy);
            g_inCapture = false;
            return TRUE;
        }
    }

    return Real_BitBlt(hdc, x, y, cx, cy, hdcSrc, x1, y1, rop);
}

void InstallScreenCapHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"gdi32", nullptr, "BitBlt",
          ProxyBitBlt, (void**)&Real_BitBlt, "screen cap GDI" },
    };
    InstallHooks(hooks);
    Log("[Cap] BitBlt hook installed");
}
