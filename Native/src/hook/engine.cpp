#include "engine.hpp"
#include "../core/pe.hpp"
#include "../core/log.hpp"
#include "../../thirdparty/MinHook/include/MinHook.h"

// MinHook is linked via CMake (target_link_libraries)

static bool g_minhookInited = false;

int InstallHooks(const std::vector<HookEntry>& hooks) {
    int ok = 0;
    int fail = 0;
    for (auto& h : hooks) {
        if (h.type == HookType::IAT) {
            HMODULE hMod = GetModuleHandleW(h.module);
            if (!hMod) {
                Log("[Hook] IAT skip %ws (not loaded)", h.module);
                continue;
            }
            void* orig = PatchIat(hMod, h.importDll, h.func, h.proxy);
            if (orig) {
                if (h.original) *h.original = orig;
                ok++;
            } else {
                fail++;
            }
        } else {
            if (!g_minhookInited) {
                if (MH_Initialize() != MH_OK) {
                    Log("[Hook] MinHook init failed");
                    return ok;
                }
                g_minhookInited = true;
            }
            // func is already narrow (LPCSTR), no conversion needed
            MH_STATUS st = MH_CreateHookApi(h.module, h.func, h.proxy, h.original);
            if (st == MH_OK) {
                ok++;
            } else {
                fail++;
            }
        }
    }

    if (g_minhookInited && ok > 0) {
        MH_EnableHook(NULL);
    }
    Log("[Hook] installed: %d OK, %d FAIL", ok, fail);
    return ok;
}
