#include "log.hpp"
#include "pipe.hpp"
#include <windows.h>
#include <cstdarg>

// ---- internal debug log (OutputDebugString + pipe INFO) ----
void Log(const char* fmt, ...) {
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(buf, _TRUNCATE, fmt, ap);
    va_end(ap);

    OutputDebugStringA((std::string(buf) + "\n").c_str());
    PipeSend("INFO", "%s", buf);
}

// ---- rate-limited log (same text at most once per interval) ----
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
        return; // already logged recently
    g_lastHash = h;
    g_lastTick = now;

    Log("%s", buf);
}
