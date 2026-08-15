#include "filterguard.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include "../../common/pipe/pipe.h"
#include <vector>
#include <fltuser.h>

typedef HRESULT (WINAPI* FCCP_t)(PCWSTR, DWORD, const FILTER_MESSAGE_HEADER*,
    DWORD, LPCWSTR, LPOVERLAPPED, HANDLE*);
static FCCP_t Real_FilterConnectCommunicationPort = nullptr;

typedef HRESULT (WINAPI* FSM_t)(HANDLE, LPVOID, DWORD, LPVOID*, DWORD*, LPOVERLAPPED);
static FSM_t Real_FilterSendMessage = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetFilterGuardEnabled(bool enable) {
    g_enabled = enable;
}

// ---- FilterConnectCommunicationPort ----
// JiYu calls this to connect to TDFileFilter.sys kernel driver.
// We block it to prevent USB/CD/execution filtering.
static HRESULT WINAPI ProxyFilterConnectCommunicationPort(
    PCWSTR portName, DWORD options, const FILTER_MESSAGE_HEADER* msg,
    DWORD msgSize, LPCWSTR instance, LPOVERLAPPED overlapped, HANDLE* handle)
{
    if (!g_enabled)
        return Real_FilterConnectCommunicationPort(portName, options, msg, msgSize, instance, overlapped, handle);

    JY_LOGI("filterguard", "blocked FilterConnectCommunicationPort: %ws", portName ? portName : L"(null)");
    PipeSend("BLOCKED", "USB/CD/process restriction request blocked (filter port)");

    if (handle) *handle = INVALID_HANDLE_VALUE;
    return HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
}

// ---- FilterSendMessage ----
// In case the port is already connected (from before injection),
// block subsequent messages.
static HRESULT WINAPI ProxyFilterSendMessage(HANDLE port,
    LPVOID msg, DWORD msgSize, LPVOID* reply,
    DWORD* replySize, LPOVERLAPPED overlapped)
{
    if (!g_enabled)
        return Real_FilterSendMessage(port, msg, msgSize, reply, replySize, overlapped);

    JY_LOGI("filterguard", "blocked FilterSendMessage to port 0x%p (size=%lu)", port, msgSize);
    PipeSend("BLOCKED", "FilterSendMessage blocked (USB/CD/exec rule)");
    return HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
}

// Restore USB/CD if already blocked (call after hooks installed)
static void RestoreUSBAndCD() {
    JY_LOGI("filterguard", "attempting to restore USB/CD access...");

    HMODULE hUsb = GetModuleHandleW(L"LibTDUsbHook10.dll");
    if (hUsb) {
        typedef void (__cdecl* FreeFn)();
        auto TDUsbFiltFree = (FreeFn)GetProcAddress(hUsb, "TDUsbFiltFree");
        if (TDUsbFiltFree) {
            TDUsbFiltFree();
            PipeSend("INFO", "USB access restored (TDUsbFiltFree)");
        }
        auto TDCdFiltFree = (FreeFn)GetProcAddress(hUsb, "TDCdFiltFree");
        if (TDCdFiltFree) {
            TDCdFiltFree();
            JY_LOGI("filterguard", "called TDCdFiltFree() - CD unblocked");
            PipeSend("INFO", "CD/DVD access restored (TDCdFiltFree)");
        }
    } else {
        JY_LOGD("filterguard", "LibTDUsbHook10.dll not loaded yet");
    }
}

void InstallFilterGuardHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"fltlib", nullptr, "FilterConnectCommunicationPort",
          ProxyFilterConnectCommunicationPort, (void**)&Real_FilterConnectCommunicationPort,
          "filter connect" },
        { HookType::MinHook, L"fltlib", nullptr, "FilterSendMessage",
          ProxyFilterSendMessage, (void**)&Real_FilterSendMessage,
          "filter send" },
    };
    InstallHooks(hooks);
    JY_LOGI("filterguard", "filter guard hooks installed (CommPort + SendMessage blocked)");

    // restore USB/CD if already blocked before injection
    RestoreUSBAndCD();
}
