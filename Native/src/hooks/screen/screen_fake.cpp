#include "screen_fake.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include "../../common/config/config.h"
#include <gdiplus.h>
#include <vector>
// gdiplus.lib linked via CMake

using namespace Gdiplus;

typedef int (*EncodeToJPEG_t)(void*, int, int, int, void*, int*, int);
static EncodeToJPEG_t Real_EncodeToJPEG = nullptr;

// Original full-size screen.png data (kept for rescaling)
static Bitmap*      g_origBmp      = nullptr;
static ULONG_PTR    g_gdiplusToken = 0;
static bool         g_fakeLoaded   = false;

// Cached rescaled image per target size (key = packed W|H|Stride)
struct CacheKey {
    int w, h, stride;
    bool operator<(const CacheKey& o) const {
        if (w != o.w) return w < o.w;
        if (h != o.h) return h < o.h;
        return stride < o.stride;
    }
};
static BYTE*    g_cacheBmp  = nullptr;
static CacheKey g_cacheKey  = {0, 0, 0};
static int      g_cacheSize = 0;

// forward decl (defined below)
static void LoadFakeImage();

// Reload the fake screen image from screen.png (called on "SCREEN_RELOAD"
// pipe command after the app replaced the file). Thread-safety: GDI+ is
// used lazily inside proxy calls; reload here replaces the cached bitmap.
void ReloadScreenFake() {
    if (g_origBmp) {
        delete g_origBmp;
        g_origBmp = nullptr;
    }
    if (g_cacheBmp) {
        free(g_cacheBmp);
        g_cacheBmp = nullptr;
        g_cacheSize = 0;
    }
    g_cacheKey = {0, 0, 0};
    g_fakeLoaded = false;
    LoadFakeImage();
    if (g_origBmp)
        JY_LOGI("screenfake", "fake screen reloaded: %dx%d", g_origBmp->GetWidth(), g_origBmp->GetHeight());
    else
        JY_LOGW("screenfake", "fake screen reload FAILED");
}

// Release the fake-screen resources (and GDI+ file handle on screen.png).
// Called from BypassStop/BypassUnhook so the file is no longer locked
// after unload; next EncodeToJPEG call re-initializes GDI+ and reloads.
void ReleaseScreenFake() {
    if (g_origBmp) {
        delete g_origBmp;
        g_origBmp = nullptr;
    }
    if (g_cacheBmp) {
        free(g_cacheBmp);
        g_cacheBmp = nullptr;
        g_cacheSize = 0;
    }
    g_cacheKey = {0, 0, 0};
    g_fakeLoaded = false;
    if (g_gdiplusToken) {
        GdiplusShutdown(g_gdiplusToken);
        g_gdiplusToken = 0;
    }
}

// Load screen.png via GDI+, keep original Bitmap for rescaling
static void LoadFakeImage() {
    if (g_fakeLoaded) return;
    g_fakeLoaded = true;

    Config& cfg = GetConfig();
    JY_LOGI("screenfake", "loading fake image: %ws", cfg.screenPngPath);

    GdiplusStartupInput si;
    GdiplusStartup(&g_gdiplusToken, &si, nullptr);

    g_origBmp = Bitmap::FromFile(cfg.screenPngPath);
    if (!g_origBmp || g_origBmp->GetLastStatus() != Ok) {
        JY_LOGW("screenfake", "failed to load screen.png");
        delete g_origBmp;
        g_origBmp = nullptr;
        return;
    }

    JY_LOGI("screenfake", "screen.png loaded: %dx%d", g_origBmp->GetWidth(), g_origBmp->GetHeight());
}

// Rescale the original image to target (w x h) BGR24 with given stride
static BYTE* GetRescaled(int w, int h, int stride) {
    if (!g_origBmp) return nullptr;

    CacheKey key = {w, h, stride};
    if (g_cacheBmp && key.w == g_cacheKey.w && key.h == g_cacheKey.h && key.stride == g_cacheKey.stride) {
        return g_cacheBmp;
    }

    // free old cache
    free(g_cacheBmp);
    g_cacheBmp = nullptr;

    int bufSize = stride * h;
    BYTE* buf = (BYTE*)malloc(bufSize);
    if (!buf) return nullptr;

    // create a scaled bitmap using GDI+
    Bitmap* scaled = new Bitmap(w, h, PixelFormat24bppRGB);
    Graphics g(scaled);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.DrawImage(g_origBmp, 0, 0, w, h);

    // lock bits and copy (BGR byte order)
    BitmapData bd;
    Rect r(0, 0, w, h);
    if (scaled->LockBits(&r, ImageLockModeRead, PixelFormat24bppRGB, &bd) == Ok) {
        BYTE* src = (BYTE*)bd.Scan0;
        int srcStride = bd.Stride;
        for (int y = 0; y < h; y++) {
            BYTE* sRow = src + y * srcStride;
            BYTE* dRow = buf + y * stride;
            for (int x = 0; x < w; x++) {
                dRow[x * 3 + 0] = sRow[x * 3 + 2]; // B <- R (swap)
                dRow[x * 3 + 1] = sRow[x * 3 + 1]; // G
                dRow[x * 3 + 2] = sRow[x * 3 + 0]; // R <- B (swap)
            }
        }
        scaled->UnlockBits(&bd);
    }

    delete scaled;

    g_cacheBmp = buf;
    g_cacheKey = key;
    g_cacheSize = bufSize;

    JY_LOGD("screenfake", "rescaled to %dx%d stride=%d (%d bytes)", w, h, stride, bufSize);
    return buf;
}

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetScreenFakeEnabled(bool enable) {
    g_enabled = enable;
}

// 7-parameter signature matching the real EncodeToJPEGBuffer
static int ProxyEncodeToJPEG(void* input, int w, int h, int stride,
                              void* output, int* outSize, int quality)
{
    if (!g_enabled) return Real_EncodeToJPEG(input, w, h, stride, output, outSize, quality);

    static bool logged = false;
    if (!logged) {
        logged = true;
        JY_LOGI("screenfake", "EncodeToJPEGBuffer: %dx%d stride=%d quality=%d",
                w, h, stride, quality);
    }

    if (!g_fakeLoaded) {
        LoadFakeImage();
    }

    if (input) {
        BYTE* fake = GetRescaled(w, h, stride);
        if (fake) {
            memcpy(input, fake, stride * h);
        } else if (g_origBmp) {
            // source loaded but rescale failed - fill green
            memset(input, 0, stride * h);
            BYTE* line = (BYTE*)input;
            for (int y = 0; y < h; y++)
                line[y * stride + 1] = 0xC0; // G
        } else {
            // no image loaded - fill blue
            memset(input, 0, stride * h);
            BYTE* line = (BYTE*)input;
            for (int y = 0; y < h; y++)
                line[y * stride] = 0x80; // B
        }
    }

    return Real_EncodeToJPEG(input, w, h, stride, output, outSize, quality);
}

void InstallScreenFakeHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"LibJPEG20.dll", nullptr, "EncodeToJPEGBuffer",
          ProxyEncodeToJPEG, (void**)&Real_EncodeToJPEG, "screen fake" },
    };
    InstallHooks(hooks);
}
