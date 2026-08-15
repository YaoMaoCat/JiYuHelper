#pragma once
#include <windows.h>
#include <string>
#include <vector>

enum class HookType { IAT, MinHook };

struct HookEntry {
    HookType      type;
    const wchar_t* module;     // module to patch (IAT: Jiyu DLL; MinHook: system DLL)
    const char*    importDll;  // import DLL name (only for IAT, e.g. "user32.dll")
    const char*    func;       // function name to hook
    void*          proxy;      // proxy function
    void**         original;   // output: original function ptr
    const char*    desc;       // description for logging
};

int InstallHooks(const std::vector<HookEntry>& hooks);
