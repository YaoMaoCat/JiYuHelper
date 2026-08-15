#include "netfilter.h"
#include "../log/log.h"

// See header for the IOCTL layout analysis. All values zeroed =>
// driver stays in "no filtering" mode.

void ClearNetFilter() {
    HANDLE h = CreateFileW(L"\\\\.\\tdnetfilter",
        GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) {
        JY_LOGD("netfilter", "cannot open \\\\.\\tdnetfilter (err=%lu), clear skipped", GetLastError());
        return;
    }

    DWORD ret = 0;

    // 1) main switch: 128-DWORD table + proxy port (0x7f30) + enable flag (0x7f34) = 0
    char table[0x208] = {0};
    DeviceIoControl(h, 0x120004, table, sizeof(table), nullptr, 0, &ret, nullptr);

    // 2) mode = 0 (no filtering); structure with process-count 0
    char mode[0x98] = {0};
    DeviceIoControl(h, 0x120018, mode, sizeof(mode), nullptr, 0, &ret, nullptr);

    // 3) clear whitelist/blacklist process rule lists
    DeviceIoControl(h, 0x120010, nullptr, 0, nullptr, 0, &ret, nullptr);
    DeviceIoControl(h, 0x120014, nullptr, 0, nullptr, 0, &ret, nullptr);

    CloseHandle(h);
    JY_LOGI("netfilter", "TDNetFilter cleared (mode=0, port=0, enable=0, lists empty)");
}
