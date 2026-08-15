#pragma once
#include <windows.h>

// ============================================================================
// Named pipe server -- duplex channel with the JiYuHelper app.
//
// Outbound events: one line "KIND|message\n", UTF-8:
//     KIND = LOADED | HOOK | BLOCKED | HEARTBEAT | ERROR | INFO
// Inbound commands (from the app):
//     UPDATE|0x<hexmask>   -> hot-update feature mask (see common/config/features.h)
//     SCREEN_RELOAD        -> reload fake screen image
//
// Pipe names (fixed contract):
//   jyhelper_main.dll   -> \\.\pipe\JYHookHelper
//   jyhelper_master.dll -> \\.\pipe\JYMasterHelper
//
// Multi-client: an accept thread + per-client reader threads + one sender
// thread that flushes queued lines in batched WriteFile bursts. When no
// client is connected the queue is kept, so events produced before the app
// connects are replayed on connect.
// ============================================================================

// Command callback: invoked for each inbound command line (not null-terminated
// guarantees: each line is a NUL-terminated C string).
typedef void (*PipeCommandHandler)(const char* cmd);

// Start the pipe server threads (idempotent; re-invocable after shutdown).
// cmdHandler is optional (nullptr to ignore inbound commands).
void PipeInit(const wchar_t* pipeName, PipeCommandHandler cmdHandler);

// Enqueue one structured event line. Non-blocking, never fails the caller;
// drops the oldest line when the queue (cap 512) is full.
void PipeSend(const char* kind, const char* fmt, ...);

// Stop all pipe threads (server accept, sender, client readers).
// Call from BypassShutdown BEFORE the DLL is unloaded; safe unload
// requires no live threads inside the module.
void PipeShutdown();
