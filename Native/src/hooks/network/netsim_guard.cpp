#include "netsim_guard.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include "../../common/pipe/pipe.h"
#include <string>
#include <vector>

// ============================================================================
// Web-filter bypass (StudentMain.exe side).
//
// TDNetFilter.sys (kernel WFP callout driver) redirects port 80/443 traffic
// to a local proxy (127.0.0.1:<port>) which serves the block page.
// Filtering is activated by DeviceIoControl:
//   0x120004  -> 0x208 B: 128-DWORD whitelist table + proxy port (0x7f30)
//                + filter-enable flag (0x7f34); 0x7f34>0 enables redirect
//   0x120008  -> whitelist mode + process rules (mode=1)
//   0x12000c  -> blacklist mode + process rules (mode=2)
// ============================================================================

typedef BOOL (WINAPI* DeviceIoControl_t)(HANDLE, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
static DeviceIoControl_t Real_DeviceIoControl = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetNetFilterEnabled(bool enable) {
    g_enabled = enable;
    // Note: active clearing is done by bypass_master (SYSTEM) only -
    // StudentMain has no permission to open the driver (err=5) and would
    // just spam the log.
}

static bool IsTdNetFilterHandle(HANDLE hDevice) {
    wchar_t path[MAX_PATH] = {0};
    DWORD len = GetFinalPathNameByHandleW(hDevice, path, MAX_PATH, VOLUME_NAME_NT);
    if (len == 0) return false;
    std::wstring ws(path);
    for (auto& c : ws) c = towlower(c);
    return ws.find(L"tdnetfilter") != std::wstring::npos;
}

// Block the filter-ENABLING IOCTLs only (main switch + whitelist/blacklist
// mode); allow disable/clear codes (0x120018/0x120010/0x120014) through.
static bool IsFilterEnableCode(DWORD code) {
    return code == 0x120004 || code == 0x120008 || code == 0x12000c;
}

static BOOL WINAPI ProxyDeviceIoControl(HANDLE hDevice, DWORD code,
    LPVOID in, DWORD inSize, LPVOID out, DWORD outSize, LPDWORD ret, LPOVERLAPPED ov)
{
    if (!g_enabled)
        return Real_DeviceIoControl(hDevice, code, in, inSize, out, outSize, ret, ov);

    if (IsTdNetFilterHandle(hDevice) && IsFilterEnableCode(code)) {
        JY_LOGI("netsim", "blocked DeviceIoControl to TDNetFilter (code=0x%lX)", code);
        PipeSend("BLOCKED", "TDNetFilter rule delivery blocked (IOCTL=0x%lX)", code);
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    return Real_DeviceIoControl(hDevice, code, in, inSize, out, outSize, ret, ov);
}

void InstallNetFilterGuard() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"kernel32", nullptr, "DeviceIoControl",
          ProxyDeviceIoControl, (void**)&Real_DeviceIoControl, "netfilter DIO" },
    };
    InstallHooks(hooks);
    JY_LOGI("netsim", "net filter guard installed");
}
