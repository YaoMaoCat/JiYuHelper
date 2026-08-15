#pragma once
#include <windows.h>

struct Config {
    wchar_t screenPngPath[MAX_PATH];
    bool    enableKeyboardBypass;
    bool    enableTopmostBlock;
    bool    enableAppListBlock;
    bool    enableProcListBlock;
    bool    enableScreenFake;
    bool    enableRemoteBlock;
    bool    enableBlackMonitor;
    bool    enableNetSimBlock;   // MasterHelper: BeginSimulate/StopSimulate/DeviceIoControl
};

Config& GetConfig();
void    LoadConfig();
