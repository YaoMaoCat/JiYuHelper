#pragma once
#include <cstdio>
#include <string>

// Debug log: OutputDebugString + named pipe (INFO event, see core/pipe.hpp).
// No "[Bp]"/"[Master]" prefix here - the app already tags events with the
// source process name. Safe to call before PipeInit (pipe layer no-ops).
void Log(const char* fmt, ...);

// Rate-limited log: the same message (hash of its text) is emitted at most
// once per intervalMs. Use for high-frequency events (focus steals, key
// hook blocks, SendInput bursts) that would otherwise flood the log.
void LogThrottled(int intervalMs, const char* fmt, ...);
