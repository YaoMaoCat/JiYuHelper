#include "pipe.hpp"
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <deque>
#include <string>
#include <vector>

// ---- server state ----
static CRITICAL_SECTION  g_lock;
static std::vector<HANDLE> g_clients;
static std::deque<std::string> g_queue;   // pending event lines
static HANDLE             g_queueEvent = nullptr;
static volatile bool     g_inited = false;
static volatile bool     g_shutdown = false;
static wchar_t           g_pipeName[128] = {0};
static PipeCommandHandler g_cmdHandler = nullptr;
static HANDLE            g_serverPipe = INVALID_HANDLE_VALUE;

// Queue cap: if the app stops reading, drop oldest lines instead of
// blocking the calling (student process UI) thread on WriteFile.
static const size_t g_queueCap = 512;

// ---- sender thread ----
// Flushes ALL pending lines in one WriteFile burst (byte-stream pipe).
// Blocking WriteFile happens HERE, never on the thread that called
// PipeSend() (which may be the student process's UI thread).
// When no client is connected the queue is KEPT (events produced before
// the app connects are replayed on connect).
static DWORD WINAPI SendThread(LPVOID) {
    while (!g_shutdown) {
        WaitForSingleObject(g_queueEvent, INFINITE);
        if (g_shutdown) break;

        std::string pending;
        std::vector<HANDLE> clients;
        EnterCriticalSection(&g_lock);
        clients = g_clients;
        if (!clients.empty() && !g_queue.empty()) {
            // 批量: 一次取出全部待发行合并写入, 大幅提升吞吐
            for (auto& s : g_queue) pending += s;
            g_queue.clear();
            ResetEvent(g_queueEvent);
        }
        LeaveCriticalSection(&g_lock);

        if (pending.empty()) {
            if (clients.empty()) Sleep(200); // no client yet: keep queue
            continue;
        }

        for (auto it = clients.begin(); it != clients.end(); ) {
            DWORD written = 0;
            if (!WriteFile(*it, pending.data(), (DWORD)pending.size(), &written, nullptr)) {
                EnterCriticalSection(&g_lock);
                for (auto r = g_clients.begin(); r != g_clients.end(); ++r) {
                    if (*r == *it) { g_clients.erase(r); break; }
                }
                LeaveCriticalSection(&g_lock);
                CloseHandle(*it);
                it = clients.erase(it);
            } else {
                ++it;
            }
        }
    }
    return 0;
}

// Per-client read thread: processes inbound command lines.
// On normal disconnect the handle is removed+closed; during shutdown
// PipeShutdown already closed the handles so we skip cleanup.
static DWORD WINAPI ClientReadThread(LPVOID param) {
    HANDLE hPipe = (HANDLE)param;
    char buf[1024];
    DWORD bytes = 0;

    while (!g_shutdown && ReadFile(hPipe, buf, sizeof(buf) - 1, &bytes, nullptr) && bytes > 0) {
        buf[bytes] = 0;
        char* line = buf;
        for (char* p = buf; ; p++) {
            if (*p == '\n' || *p == 0) {
                char saved = *p;
                *p = 0;
                if (*line && g_cmdHandler) g_cmdHandler(line);
                if (!saved) break;
                line = p + 1;
            }
        }
    }

    if (!g_shutdown) {
        EnterCriticalSection(&g_lock);
        for (auto it = g_clients.begin(); it != g_clients.end(); ++it) {
            if (*it == hPipe) { g_clients.erase(it); break; }
        }
        LeaveCriticalSection(&g_lock);
        CloseHandle(hPipe);
    }
    return 0;
}

// Accept loop: creates a new pipe instance, waits for a client,
// starts a read thread, then loops to accept more.
static DWORD WINAPI ServerThread(LPVOID) {
    while (!g_shutdown) {
        HANDLE hPipe = CreateNamedPipeW(
            g_pipeName,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            65536, 65536, 0, nullptr);
        if (hPipe == INVALID_HANDLE_VALUE) {
            if (g_shutdown) break;
            Sleep(1000);
            continue;
        }

        g_serverPipe = hPipe;
        if (!ConnectNamedPipe(hPipe, nullptr)) {
            if (GetLastError() != ERROR_PIPE_CONNECTED) {
                g_serverPipe = INVALID_HANDLE_VALUE;
                CloseHandle(hPipe);
                if (g_shutdown) break;
                continue;
            }
        }
        g_serverPipe = INVALID_HANDLE_VALUE;
        if (g_shutdown) {
            CloseHandle(hPipe);
            break;
        }

        EnterCriticalSection(&g_lock);
        g_clients.push_back(hPipe);
        LeaveCriticalSection(&g_lock);

        HANDLE hRead = CreateThread(nullptr, 0, ClientReadThread, hPipe, 0, nullptr);
        if (hRead) CloseHandle(hRead);
    }
    return 0;
}

void PipeInit(const wchar_t* pipeName, PipeCommandHandler cmdHandler) {
    if (g_inited) {
        // 重新启用 (BypassStop 后): 重置关闭标志, 重新创建线程
        g_shutdown = false;
    } else {
        wcsncpy_s(g_pipeName, pipeName ? pipeName : L"", _TRUNCATE);
        g_cmdHandler = cmdHandler;
        InitializeCriticalSection(&g_lock);
        g_queueEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        g_inited = true;
    }

    HANDLE h = CreateThread(nullptr, 0, ServerThread, nullptr, 0, nullptr);
    if (h) CloseHandle(h);
    h = CreateThread(nullptr, 0, SendThread, nullptr, 0, nullptr);
    if (h) CloseHandle(h);
}

// Non-blocking: enqueue and return. Never blocks the caller.
void PipeSend(const char* kind, const char* fmt, ...) {
    if (!g_inited || g_shutdown) return;

    char msg[1024];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf_s(msg, _TRUNCATE, fmt, ap);
    va_end(ap);

    char line[2048];
    sprintf_s(line, "%s|%s\n", kind ? kind : "INFO", msg);

    EnterCriticalSection(&g_lock);
    if (g_queue.size() >= g_queueCap) {
        g_queue.pop_front(); // drop oldest, keep newest
    }
    g_queue.push_back(line);
    SetEvent(g_queueEvent);
    LeaveCriticalSection(&g_lock);
}

void PipeShutdown() {
    if (!g_inited) return;
    g_shutdown = true;

    // interrupt ServerThread's ConnectNamedPipe
    if (g_serverPipe != INVALID_HANDLE_VALUE) {
        CloseHandle(g_serverPipe);
        g_serverPipe = INVALID_HANDLE_VALUE;
    }

    // interrupt ClientReadThreads' ReadFile
    EnterCriticalSection(&g_lock);
    for (auto h : g_clients) CloseHandle(h);
    g_clients.clear();
    LeaveCriticalSection(&g_lock);

    // wake SendThread so it can check g_shutdown
    SetEvent(g_queueEvent);

    // give all pipe threads a moment to exit before the module is unloaded
    Sleep(600);
}
