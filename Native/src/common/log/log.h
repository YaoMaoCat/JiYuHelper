#pragma once
#include <cstdio>

// ============================================================================
// Unified logging.
//
// Levels and sink policy:
//   JY_LEVEL_DEBUG -> OutputDebugStringA only (verbose internals, no pipe)
//   JY_LEVEL_INFO  -> OutputDebugStringA + pipe INFO event
//   JY_LEVEL_WARN  -> OutputDebugStringA + pipe INFO event (message tagged [WARN])
//   JY_LEVEL_ERROR -> OutputDebugStringA + pipe ERROR event
//
// Safe to call before PipeInit (pipe layer no-ops until then).
// module is a short tag like "config"/"pipe"/"topmost" used as [module].
// ============================================================================

enum JyLogLevel {
    JY_LEVEL_DEBUG = 0,
    JY_LEVEL_INFO,
    JY_LEVEL_WARN,
    JY_LEVEL_ERROR
};

// level, module, printf-style fmt.
void JyLog(JyLogLevel level, const char* module, const char* fmt, ...);

// Rate-limited variant: identical text (hash of the formatted line) is
// emitted at most once per intervalMs. Use for high-frequency events
// (focus steals, key blocks, SendInput bursts) that would flood the log.
void JyLogThrottled(int intervalMs, JyLogLevel level, const char* module, const char* fmt, ...);

#define JY_LOGD(module, ...) JyLog(JY_LEVEL_DEBUG, module, __VA_ARGS__)
#define JY_LOGI(module, ...) JyLog(JY_LEVEL_INFO,  module, __VA_ARGS__)
#define JY_LOGW(module, ...) JyLog(JY_LEVEL_WARN,  module, __VA_ARGS__)
#define JY_LOGE(module, ...) JyLog(JY_LEVEL_ERROR, module, __VA_ARGS__)

// throttled INFO-level log; intervalMs = minimum gap between two emits
#define JY_LOGT(intervalMs, module, ...) JyLogThrottled(intervalMs, JY_LEVEL_INFO, module, __VA_ARGS__)
