#include "procguard.hpp"
#include "engine.hpp"
#include "../core/log.hpp"
#include "../core/pipe.hpp"
#include <string>

// ============================================================
// Hook process/shutdown APIs used by StudentMain.exe to
// execute teacher commands.
//
// From PE analysis of StudentMain.exe:
//   CreateProcessW  (kernel32)  — launch processes
//   CreateProcessAsUserW (advapi32) — launch as user
//   WinExec         (kernel32)  — legacy execute
//   ShellExecuteW   (shell32)   — shell execute
//   ShellExecuteExW (shell32)   — shell execute (extended)
//   TerminateProcess (kernel32) — kill processes
//   ExitWindowsEx   (user32)    — shutdown (from Shutdown.exe)
// ============================================================

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetProcGuardEnabled(bool enable) {
    g_enabled = enable;
    
}

static void NotifyBlock(const wchar_t* action, const wchar_t* detail) {
    Log("[Guard] Blocked %ws: %ws", action, detail);
    PipeSend("BLOCKED", "Blocked %ls: %ls", action, detail);
}

// ---- CreateProcessW ----
typedef BOOL (WINAPI* CPW_t)(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES,
    LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);
static CPW_t Real_CreateProcessW = nullptr;

static BOOL WINAPI ProxyCreateProcessW(LPCWSTR app, LPWSTR cmd, LPSECURITY_ATTRIBUTES sa,
    LPSECURITY_ATTRIBUTES sta, BOOL ih, DWORD flags, LPVOID env, LPCWSTR dir,
    LPSTARTUPINFOW si, LPPROCESS_INFORMATION pi)
{
    if (!g_enabled) return Real_CreateProcessW(app, cmd, sa, sta, ih, flags, env, dir, si, pi);
    const wchar_t* name = app ? app : (cmd ? cmd : L"(null)");
    NotifyBlock(L"CreateProcess", name);
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ---- CreateProcessAsUserW ----
typedef BOOL (WINAPI* CPAU_t)(HANDLE, LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES,
    LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);
static CPAU_t Real_CreateProcessAsUserW = nullptr;

static BOOL WINAPI ProxyCreateProcessAsUserW(HANDLE token, LPCWSTR app, LPWSTR cmd,
    LPSECURITY_ATTRIBUTES sa, LPSECURITY_ATTRIBUTES sta, BOOL ih, DWORD flags,
    LPVOID env, LPCWSTR dir, LPSTARTUPINFOW si, LPPROCESS_INFORMATION pi)
{
    if (!g_enabled) return Real_CreateProcessAsUserW(token, app, cmd, sa, sta, ih, flags, env, dir, si, pi);
    const wchar_t* name = app ? app : (cmd ? cmd : L"(null)");
    NotifyBlock(L"CreateProcessAsUser", name);
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ---- WinExec ----
typedef UINT (WINAPI* WE_t)(LPCSTR, UINT);
static WE_t Real_WinExec = nullptr;

static UINT WINAPI ProxyWinExec(LPCSTR cmd, UINT show) {
    if (!g_enabled) return Real_WinExec(cmd, show);
    char buf[256] = {0};
    strncpy_s(buf, cmd ? cmd : "", _TRUNCATE);
    wchar_t wbuf[256];
    MultiByteToWideChar(CP_ACP, 0, buf, -1, wbuf, 256);
    NotifyBlock(L"WinExec", wbuf);
    SetLastError(ERROR_ACCESS_DENIED);
    return 0; // ERROR_INVALID_PARAMS
}

// ---- ShellExecuteW ----
typedef HINSTANCE (WINAPI* SE_t)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT);
static SE_t Real_ShellExecuteW = nullptr;

static HINSTANCE WINAPI ProxyShellExecuteW(HWND hwnd, LPCWSTR op, LPCWSTR file,
    LPCWSTR params, LPCWSTR dir, INT show)
{
    if (!g_enabled) return Real_ShellExecuteW(hwnd, op, file, params, dir, show);
    NotifyBlock(L"ShellExecute", file ? file : L"(null)");
    SetLastError(ERROR_ACCESS_DENIED);
    return (HINSTANCE)(INT_PTR)SE_ERR_ACCESSDENIED; // 32 = access denied
}

// ---- ShellExecuteExW ----
typedef BOOL (WINAPI* SEEW_t)(LPSHELLEXECUTEINFOW);
static SEEW_t Real_ShellExecuteExW = nullptr;

static BOOL WINAPI ProxyShellExecuteExW(LPSHELLEXECUTEINFOW sei) {
    if (!g_enabled) return Real_ShellExecuteExW(sei);
    if (sei && sei->lpFile) {
        NotifyBlock(L"ShellExecuteEx", sei->lpFile);
    } else {
        NotifyBlock(L"ShellExecuteEx", L"(null)");
    }
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ---- TerminateProcess ----
typedef BOOL (WINAPI* TP_t)(HANDLE, UINT);
static TP_t Real_TerminateProcess = nullptr;

static BOOL WINAPI ProxyTerminateProcess(HANDLE hProcess, UINT exitCode) {
    if (!g_enabled) return Real_TerminateProcess(hProcess, exitCode);
    DWORD pid = GetProcessId(hProcess);
    DWORD ourPid = GetCurrentProcessId();
    
    // Don't block killing our own process (that would be weird)
    if (pid == ourPid) {
        return Real_TerminateProcess(hProcess, exitCode);
    }
    
    wchar_t name[64];
    swprintf_s(name, L"PID=%lu (exit=%u)", pid, exitCode);
    NotifyBlock(L"TerminateProcess", name);
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ---- ExitWindowsEx ----
typedef BOOL (WINAPI* EWE_t)(UINT, DWORD);
static EWE_t Real_ExitWindowsEx = nullptr;

static BOOL WINAPI ProxyExitWindowsEx(UINT flags, DWORD reason) {
    if (!g_enabled) return Real_ExitWindowsEx(flags, reason);
    NotifyBlock(L"ExitWindows", L"Shutdown/Restart blocked");
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

void InstallProcGuardHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"kernel32", nullptr, "CreateProcessW",
          ProxyCreateProcessW, (void**)&Real_CreateProcessW, "proc CProcessW" },
        { HookType::MinHook, L"advapi32", nullptr, "CreateProcessAsUserW",
          ProxyCreateProcessAsUserW, (void**)&Real_CreateProcessAsUserW, "proc CProcAsUser" },
        { HookType::MinHook, L"kernel32", nullptr, "WinExec",
          ProxyWinExec, (void**)&Real_WinExec, "proc WinExec" },
        { HookType::MinHook, L"shell32", nullptr, "ShellExecuteW",
          ProxyShellExecuteW, (void**)&Real_ShellExecuteW, "proc ShellExecW" },
        { HookType::MinHook, L"shell32", nullptr, "ShellExecuteExW",
          ProxyShellExecuteExW, (void**)&Real_ShellExecuteExW, "proc ShellExecExW" },
        { HookType::MinHook, L"kernel32", nullptr, "TerminateProcess",
          ProxyTerminateProcess, (void**)&Real_TerminateProcess, "proc Terminate" },
        { HookType::MinHook, L"user32", nullptr, "ExitWindowsEx",
          ProxyExitWindowsEx, (void**)&Real_ExitWindowsEx, "proc Shutdown" },
    };
    InstallHooks(hooks);
    Log("[Guard] Process/shutdown guard hooks installed");
}
