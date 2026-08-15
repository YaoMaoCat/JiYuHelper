#pragma once
#include <windows.h>
#include <string>
#include <vector>

// ============================================================================
// Hook engine -- two patch strategies:
//   IAT     : rewrite an import slot inside a loaded Jiyu module
//             (module = the Jiyu DLL, importDll = e.g. "user32.dll")
//   MinHook : inline trampoline on an exported API
//             (module = the system DLL, func = export name)
//
// MinHook library: thirdparty/MinHook (linked via CMake).
// ============================================================================

enum class HookType { IAT, MinHook };

struct HookEntry {
    HookType       type;
    const wchar_t* module;     // module to patch (IAT: Jiyu DLL; MinHook: system DLL)
    const char*    importDll;  // import DLL name (only for IAT, e.g. "user32.dll")
    const char*    func;       // function name to hook
    void*          proxy;      // proxy function
    void**         original;   // output: original function ptr
    const char*    desc;       // description for logging
};

// Install all hooks; IAT entries whose module is not loaded are skipped
// (not counted as failure). MinHook entries are enabled in one batch.
// Returns number of hooks successfully installed.
int InstallHooks(const std::vector<HookEntry>& hooks);
