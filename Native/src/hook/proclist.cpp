#include "proclist.hpp"
#include "engine.hpp"
#include "../core/log.hpp"
#include <tlhelp32.h>
#include <string>
#include <vector>
#include <set>
#include <psapi.h>
#include <winternl.h>

// Hook Process32FirstW/NextW AND EnumProcesses to hide user-space processes.
// StudentMain uses BOTH toolhelp AND psapi to enumerate processes.

typedef BOOL(WINAPI* P32F_t)(HANDLE, LPPROCESSENTRY32W);
typedef P32F_t P32N_t;
static P32F_t Real_P32F = nullptr;
static P32N_t Real_P32N = nullptr;

typedef BOOL(WINAPI* EP_t)(DWORD*, DWORD, DWORD*);
static EP_t Real_EnumProcesses = nullptr;

static std::set<std::wstring> g_whitelist;
static bool g_whitelistInited = false;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetProcListEnabled(bool enable) {
    g_enabled = enable;
    
}

static void InitWhitelist() {
    if (g_whitelistInited) return;
    g_whitelistInited = true;

    g_whitelist.insert(L"System");
    g_whitelist.insert(L"System Idle Process");
    g_whitelist.insert(L"[System Process]");
    g_whitelist.insert(L"Registry");
    g_whitelist.insert(L"Memory Compression");
    g_whitelist.insert(L"smss.exe");
    g_whitelist.insert(L"csrss.exe");
    g_whitelist.insert(L"wininit.exe");
    g_whitelist.insert(L"winlogon.exe");
    g_whitelist.insert(L"services.exe");
    g_whitelist.insert(L"lsass.exe");
    g_whitelist.insert(L"svchost.exe");
    g_whitelist.insert(L"fontdrvhost.exe");
    g_whitelist.insert(L"spoolsv.exe");
    g_whitelist.insert(L"sihost.exe");
    g_whitelist.insert(L"taskhostw.exe");
    g_whitelist.insert(L"ctfmon.exe");
    g_whitelist.insert(L"RuntimeBroker.exe");
    g_whitelist.insert(L"SecurityHealthService.exe");
    g_whitelist.insert(L"securityhealthsystray.exe");
    g_whitelist.insert(L"explorer.exe");
    g_whitelist.insert(L"SearchIndexer.exe");
    g_whitelist.insert(L"SearchHost.exe");
    g_whitelist.insert(L"ShellExperienceHost.exe");
    g_whitelist.insert(L"StartMenuExperienceHost.exe");
    g_whitelist.insert(L"Widgets.exe");
    g_whitelist.insert(L"dwm.exe");
    g_whitelist.insert(L"TiWorker.exe");
    g_whitelist.insert(L"TrustedInstaller.exe");
    g_whitelist.insert(L"WmiPrvSE.exe");
    g_whitelist.insert(L"MsMpEng.exe");
    g_whitelist.insert(L"MsMpEngCP.exe");
    g_whitelist.insert(L"conhost.exe");
    g_whitelist.insert(L"CompatTelRunner.exe");
    g_whitelist.insert(L"StudentMain.exe");
    g_whitelist.insert(L"GATESRV.exe");
    g_whitelist.insert(L"MasterHelper.exe");
    g_whitelist.insert(L"SpecialSet.exe");
    g_whitelist.insert(L"TDOvrSet.exe");
    g_whitelist.insert(L"TDChalk.exe");
    g_whitelist.insert(L"Shutdown.exe");

    Log("[Proc] Whitelist built: %zu entries", g_whitelist.size());
}

static bool IsSystemProcess(const wchar_t* exe) {
    return g_whitelist.count(exe) > 0;
}

// Get process name from PID (used by EnumProcesses filter)
static bool GetProcessName(DWORD pid, wchar_t* name, DWORD nameLen) {
    HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!h) return false;
    DWORD len = nameLen;
    BOOL ok = QueryFullProcessImageNameW(h, 0, name, &len);
    CloseHandle(h);
    if (ok && len > 0) {
        wchar_t* sep = wcsrchr(name, L'\\');
        if (sep) wmemmove_s(name, nameLen, sep + 1, wcslen(sep + 1) + 1);
        return true;
    }
    return false;
}

// ---- Process32FirstW (StudentMain's PRIMARY enumeration method) ----
static BOOL WINAPI ProxyP32F(HANDLE snap, LPPROCESSENTRY32W pe) {
    if (!g_enabled) return Real_P32F(snap, pe);
    while (Real_P32F(snap, pe)) {
        if (IsSystemProcess(pe->szExeFile)) return TRUE;
    }
    return FALSE;
}

static BOOL WINAPI ProxyP32N(HANDLE snap, LPPROCESSENTRY32W pe) {
    if (!g_enabled) return Real_P32N(snap, pe);
    while (Real_P32N(snap, pe)) {
        if (IsSystemProcess(pe->szExeFile)) return TRUE;
    }
    return FALSE;
}

// ---- EnumProcesses ----
// This is the PRIMARY method StudentMain uses for process enumeration.
// We filter out non-whitelisted processes from the returned list.
static BOOL WINAPI ProxyEnumProcesses(DWORD* pids, DWORD cb, DWORD* needed) {
    if (!g_enabled) return Real_EnumProcesses(pids, cb, needed);
    // Get the original list
    BOOL result = Real_EnumProcesses(pids, cb, needed);
    if (!result || !pids || cb < sizeof(DWORD)) return result;

    DWORD count = (*needed) / sizeof(DWORD);
    DWORD writeIdx = 0;

    for (DWORD i = 0; i < count; i++) {
        DWORD pid = pids[i];
        if (pid == 0) continue;

        wchar_t name[MAX_PATH];
        if (pid == 4) {
            pids[writeIdx++] = pid;
        } else if (GetProcessName(pid, name, MAX_PATH)) {
            if (IsSystemProcess(name)) {
                pids[writeIdx++] = pid;
            }
        }
    }

    if (count > 0) {
        static bool logged = false;
        if (!logged) {
            logged = true;
            Log("[Proc] EP filtered: %lu -> %lu processes", count, writeIdx);
        }
    }

    *needed = writeIdx * sizeof(DWORD);
    return TRUE;
}

// Also hook NtQuerySystemInformation as it's the low-level API
// that all process enumeration eventually calls.
typedef NTSTATUS (NTAPI* NQSI_t)(SYSTEM_INFORMATION_CLASS, PVOID, ULONG, PULONG);
static NQSI_t Real_NtQuerySystemInformation = nullptr;

static NTSTATUS NTAPI ProxyNtQuerySystemInformation(
    SYSTEM_INFORMATION_CLASS sic, PVOID buffer, ULONG size, PULONG retLen)
{
    // Only intercept SystemProcessInformation
    if (sic == SystemProcessInformation) {
        static bool logged = false;
        if (!logged) { logged = true; Log("[Proc] NtQSI called"); }
    }
    return Real_NtQuerySystemInformation(sic, buffer, size, retLen);
}

void InstallProcListHook() {
    InitWhitelist();

    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"kernel32", nullptr, "Process32FirstW",
          ProxyP32F, (void**)&Real_P32F, "proc F" },
        { HookType::MinHook, L"kernel32", nullptr, "Process32NextW",
          ProxyP32N, (void**)&Real_P32N, "proc N" },
        { HookType::MinHook, L"psapi", nullptr, "EnumProcesses",
          ProxyEnumProcesses, (void**)&Real_EnumProcesses, "proc EP" },
    };
    InstallHooks(hooks);
    Log("[Proc] Hooks installed (P32+EP) - %zu whitelisted", g_whitelist.size());
}

