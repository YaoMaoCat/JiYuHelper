#include "guard.h"
#include "../common/hook/hook.h"
#include "../common/log/log.h"
#include "../common/pipe/pipe.h"
#include "../common/config/config.h"
#include "../common/config/features.h"
#include <string>
#include <vector>

// ============ TerminateProcess ============
typedef BOOL (WINAPI* TP_t)(HANDLE, UINT);
static TP_t Real_TerminateProcess = nullptr;

// Runtime switches (hot-update); default OFF, enabled via MasterHotUpdate()
static bool g_enableProc = false;  // TerminateProcess / CreateProcessW
static bool g_enableHook = false;  // TDProcHookEnableTerminate
static bool g_enableSim  = false;  // BeginSimulate / StopSimulate / DeviceIoControl

void MasterHotUpdate(uint64_t mask) {
    g_enableProc = (mask & FEATURE_PROCGUARD) != 0;
    g_enableHook = (mask & FEATURE_PROCHOOK) != 0;
    g_enableSim  = (mask & FEATURE_NETSIM) != 0;
}

static BOOL WINAPI ProxyTerminateProcess(HANDLE hProcess, UINT exitCode) {
    if (!g_enableProc) return Real_TerminateProcess(hProcess, exitCode);
    DWORD pid = GetProcessId(hProcess);
    DWORD ourPid = GetCurrentProcessId();
    if (pid == ourPid) return Real_TerminateProcess(hProcess, exitCode);
    JY_LOGI("master", "blocked TerminateProcess(PID=%lu)", pid);
    PipeSend("BLOCKED", "Master: TerminateProcess(PID=%lu) blocked", pid);
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ============ CreateProcessW ============
typedef BOOL (WINAPI* CPW_t)(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES,
    LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);
static CPW_t Real_CreateProcessW = nullptr;

static BOOL WINAPI ProxyCreateProcessW(LPCWSTR app, LPWSTR cmd, LPSECURITY_ATTRIBUTES sa,
    LPSECURITY_ATTRIBUTES st, BOOL inh, DWORD flags, LPVOID env,
    LPCWSTR dir, LPSTARTUPINFOW si, LPPROCESS_INFORMATION pi)
{
    if (!g_enableProc) return Real_CreateProcessW(app, cmd, sa, st, inh, flags, env, dir, si, pi);
    JY_LOGI("master", "blocked CreateProcessW: %ws", app ? app : cmd);
    PipeSend("BLOCKED", "Master: CreateProcessW(%ls) blocked", app ? app : cmd);
    SetLastError(ERROR_ACCESS_DENIED);
    return FALSE;
}

// ============ TDProcHookEnableTerminate ============
typedef void (__cdecl* TPHET_t)(BOOL);
static TPHET_t Real_TDProcHookEnableTerminate = nullptr;

static void __cdecl ProxyTDProcHookEnableTerminate(BOOL enable) {
    if (!g_enableHook) { if (Real_TDProcHookEnableTerminate) Real_TDProcHookEnableTerminate(enable); return; }
    JY_LOGI("master", "blocked TDProcHookEnableTerminate(%d)", enable);
    PipeSend("BLOCKED", "Master: TDProcHookEnableTerminate(%d) blocked", enable);
    // don't call real function = process termination stays disabled
}

// ============ BeginSimulate / StopSimulate ============
typedef void (__cdecl* Simulate_t)(DWORD);
static Simulate_t Real_BeginSimulate = nullptr;
static Simulate_t Real_StopSimulate = nullptr;

static void __cdecl ProxyBeginSimulate(DWORD param) {
    if (!g_enableSim) { if (Real_BeginSimulate) Real_BeginSimulate(param); return; }
    JY_LOGI("master", "blocked BeginSimulate(param=%lu) - internet stays unblocked", param);
    PipeSend("BLOCKED", "Master: BeginSimulate(param=%lu) blocked - internet stays unblocked", param);
    SetLastError(ERROR_ACCESS_DENIED);
}

static void __cdecl ProxyStopSimulate(DWORD param) {
    if (!g_enableSim) { if (Real_StopSimulate) Real_StopSimulate(param); return; }
    JY_LOGI("master", "StopSimulate(param=%lu) allowed", param);
    if (Real_StopSimulate) Real_StopSimulate(param);
}

// ============ DeviceIoControl (block IOCTL to TDNetFilter) ============
typedef BOOL (WINAPI* DIO_t)(HANDLE, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
static DIO_t Real_DeviceIoControl = nullptr;

static BOOL WINAPI ProxyDeviceIoControl(HANDLE hDevice, DWORD code,
    LPVOID in, DWORD inSize, LPVOID out, DWORD outSize, LPDWORD ret, LPOVERLAPPED ov)
{
    if (!g_enableSim)
        return Real_DeviceIoControl(hDevice, code, in, inSize, out, outSize, ret, ov);

    // TDNetFilter device path includes "TDNetFilter" in the name
    wchar_t devPath[MAX_PATH] = {0};
    DWORD len = GetFinalPathNameByHandleW(hDevice, devPath, MAX_PATH, VOLUME_NAME_NT);
    if (len > 0) {
        std::wstring path(devPath);
        for (auto& c : path) c = towlower(c);
        if (path.find(L"tdnetfilter") != std::wstring::npos) {
            JY_LOGI("master", "blocked DeviceIoControl to TDNetFilter (code=0x%lX)", code);
            PipeSend("BLOCKED", "Master: DeviceIoControl->TDNetFilter (code=0x%lX) blocked", code);
            SetLastError(ERROR_ACCESS_DENIED);
            return FALSE;
        }
    }
    return Real_DeviceIoControl(hDevice, code, in, inSize, out, outSize, ret, ov);
}

void InstallMasterProcGuard() {
    Config& cfg = GetConfig();

    // install ALL hooks unconditionally; runtime enable state comes from
    // MasterHotUpdate() (initial mask from config below, then pipe "UPDATE")
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"kernel32", nullptr, "TerminateProcess",
          ProxyTerminateProcess, (void**)&Real_TerminateProcess, "master TP" },
        { HookType::MinHook, L"kernel32", nullptr, "CreateProcessW",
          ProxyCreateProcessW, (void**)&Real_CreateProcessW, "master CPW" },
        { HookType::MinHook, L"LibTDProcHook32.dll", nullptr, "TDProcHookEnableTerminate",
          ProxyTDProcHookEnableTerminate, (void**)&Real_TDProcHookEnableTerminate, "master TPHET" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "BeginSimulate",
          ProxyBeginSimulate, (void**)&Real_BeginSimulate, "master BSim" },
        { HookType::MinHook, L"LibTDMaster.dll", nullptr, "StopSimulate",
          ProxyStopSimulate, (void**)&Real_StopSimulate, "master SSim" },
        { HookType::MinHook, L"kernel32", nullptr, "DeviceIoControl",
          ProxyDeviceIoControl, (void**)&Real_DeviceIoControl, "master DIO" },
    };
    InstallHooks(hooks);

    // initial enable state from hook.cfg
    uint64_t mask = 0;
    if (cfg.enableProcListBlock) mask |= FEATURE_PROCGUARD | FEATURE_PROCHOOK;
    if (cfg.enableNetSimBlock)   mask |= FEATURE_NETSIM;
    MasterHotUpdate(mask);
    JY_LOGI("master", "proc guard hooks installed (mask=0x%llX)", mask);

    // call StopSimulate immediately to unblock if already blocked
    if (cfg.enableNetSimBlock) {
        HMODULE hTDM = GetModuleHandleW(L"LibTDMaster.dll");
        if (hTDM) {
            auto StopSim = (void (__cdecl*)(DWORD))GetProcAddress(hTDM, "StopSimulate");
            if (StopSim) {
                StopSim(0);
                JY_LOGI("master", "called StopSimulate(0) - network unblocked on init");
            }
        }
    }
}
