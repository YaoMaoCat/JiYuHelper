#include "log.h"
#include "../pipe/pipe.h"
#include <windows.h>
#include <cstdarg>
#include <cstdint>
#include <cstring>

// ---- ring of recent hashes for throttling (16 slots) ----
static const int  kThrottleSlots = 16;
static uint32_t   g_hashes[kThrottleSlots];
static DWORD      g_ticks[kThrottleSlots];
static int        g_nextSlot = 0;

static uint32_t HashText(const char* s) {
    uint32_t h = 5381;
    while (*s) h = h * 33 + (unsigned char)*s++;
    return h;
}

static void Emit(JyLogLevel level, const char* module, const char* text) {
    char line[1024];
    if (module && *module) {
        sprintf_s(line, "[%s] %s", module, text);
    } else {
        sprintf_s(line, "%s", text);
    }

    OutputDebugStringA(line);

    switch (level) {
        case JY_LEVEL_INFO:
        case JY_LEVEL_WARN:
            // WARN rides the INFO pipe kind (KIND is part of the pipe contract)
            PipeSend("INFO", "%s", line);
            break;
        case JY_LEVEL_ERROR:
            PipeSend("ERROR", "%s", line);
            break;
        case JY_LEVEL_DEBUG:
        default:
            break; // debug: debugger output only, keep the pipe clean
    }
}

void JyLog(JyLogLevel level, const char* module, const char* fmt, ...) {
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(buf, _TRUNCATE, fmt, ap);
    va_end(ap);
    Emit(level, module, buf);
}

void JyLogThrottled(int intervalMs, JyLogLevel level, const char* module, const char* fmt, ...) {
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(buf, _TRUNCATE, fmt, ap);
    va_end(ap);

    DWORD now = GetTickCount();
    uint32_t h = HashText(buf);
    for (int i = 0; i < kThrottleSlots; i++) {
        if (g_hashes[i] == h && (now - g_ticks[i]) < (DWORD)intervalMs)
            return; // logged recently
    }
    g_hashes[g_nextSlot] = h;
    g_ticks[g_nextSlot] = now;
    g_nextSlot = (g_nextSlot + 1) % kThrottleSlots;

    Emit(level, module, buf);
}
