#include "filterguard.hpp"
#include "engine.hpp"
#include "../core/log.hpp"
#include "../core/pipe.hpp"
#include <string>
#include <fltuser.h>

// Hook fltlib.dll (Filter Communication Port) to block USB/CD/execution
// filtering initiated by JiYu's filter drivers.
//
// Communication flow:
//   StudentMain → LibTDUsbHook10.TDUsbFiltBlock()
//     → fltlib.FilterConnectCommunicationPort("\\.\TDFileFilterPort")
//     → fltlib.FilterSendMessage(MSG_BLOCK_USB)
//     → TDFileFilter.sys (kernel) enforces the block
//
// We intercept at FilterConnectCommunicationPort to prevent
// establishing ANY communication with the filter driver.

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
    wchar_t note[256];
    swprintf_s(note, L"Filter port: %ws", portName ? portName : L"(null)");
    Log("[Filter] Blocked FilterConnectCommunicationPort: %ws", portName);
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
    Log("[Filter] Blocked FilterSendMessage to port 0x%p (size=%lu)", port, msgSize);
    PipeSend("BLOCKED", "FilterSendMessage blocked (USB/CD/exec rule)");
    return HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
}

// Restore USB/CD if already blocked (call after hooks installed)
static void RestoreUSBAndCD() {
    Log("[Filter] Attempting to restore USB/CD access...");

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
            Log("[Filter] Called TDCdFiltFree() - CD unblocked");
            PipeSend("INFO", "CD/DVD access restored (TDCdFiltFree)");
        }
    } else {
        Log("[Filter] LibTDUsbHook10.dll not loaded yet");
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
    Log("[Filter] Filter guard hooks installed (CommPort+SendMessage blocked)");

    // Restore USB/CD if already blocked before injection
    RestoreUSBAndCD();
}
