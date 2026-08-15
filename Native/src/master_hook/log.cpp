#include "../core/log.hpp"
#include "../core/pipe.hpp"
#include <windows.h>
#include <cstdio>
#include <cstdarg>

// Log for MasterHelper: OutputDebugString + pipe INFO (JYMasterHelper).
// No "[Master]" prefix - the app tags events with the process name.
void Log(const char* fmt, ...) {
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(buf, _TRUNCATE, fmt, ap);
    va_end(ap);

    OutputDebugStringA((std::string(buf) + "\n").c_str());
    PipeSend("INFO", "%s", buf);
}

// Rate-limited variant for MasterHelper (same-hash throttle)
static uint32_t g_lastHash = 0;
static DWORD    g_lastTick = 0;

static uint32_t HashText(const char* s) {
    uint32_t h = 5381;
    while (*s) h = h * 33 + (unsigned char)*s++;
    return h;
}

void LogThrottled(int intervalMs, const char* fmt, ...) {
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(buf, _TRUNCATE, fmt, ap);
    va_end(ap);

    DWORD now = GetTickCount();
    uint32_t h = HashText(buf);
    if (h == g_lastHash && (now - g_lastTick) < (DWORD)intervalMs)
        return;
    g_lastHash = h;
    g_lastTick = now;

    Log("%s", buf);
}
