#pragma once
#include <windows.h>

// ============================================================================
// Named pipe server -- duplex channel with the JiYuHelper app.
//
// Outbound events: one line "KIND|message\n", UTF-8:
//     KIND = LOADED | HOOK | BLOCKED | HEARTBEAT | ERROR | INFO
// Inbound commands (from the app):
//     UPDATE|0x<hexmask>   -> hot-update feature mask (see core/hotupdate.hpp)
//
// Pipe names:
//   bypass_main.dll    -> \\.\pipe\JYHookHelper
//   bypass_master.dll  -> \\.\pipe\JYMasterHelper
// ============================================================================

// Command callback: invoked for each inbound command line.
typedef void (*PipeCommandHandler)(const char* cmd);

// Start the pipe server on a background thread (idempotent).
// cmdHandler is optional (nullptr to ignore inbound commands).
void PipeInit(const wchar_t* pipeName, PipeCommandHandler cmdHandler);

// Send one structured event line. No-op if PipeInit was not called.
void PipeSend(const char* kind, const char* fmt, ...);

// Stop all pipe threads (server accept, sender, client readers).
// Call from BypassShutdown BEFORE the DLL is unloaded; safe unload
// requires no live threads inside the module.
void PipeShutdown();
