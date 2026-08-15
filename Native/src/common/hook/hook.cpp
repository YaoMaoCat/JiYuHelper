#include "hook.h"
#include "../util/pe.h"
#include "../log/log.h"
#include "../../../thirdparty/MinHook/include/MinHook.h"

// MinHook is linked via CMake (target_link_libraries)

static bool g_minhookInited = false;

int InstallHooks(const std::vector<HookEntry>& hooks) {
    int ok = 0;
    int fail = 0;
    for (auto& h : hooks) {
        if (h.type == HookType::IAT) {
            HMODULE hMod = GetModuleHandleW(h.module);
            if (!hMod) {
                JY_LOGD("hook", "IAT skip %ws (not loaded)", h.module);
                continue;
            }
            void* orig = PatchIat(hMod, h.importDll, h.func, h.proxy);
            if (orig) {
                if (h.original) *h.original = orig;
                JY_LOGI("hook", "IAT %ws!%s -> %s", h.module, h.func, h.desc ? h.desc : "?");
                ok++;
            } else {
                JY_LOGW("hook", "IAT %ws!%s not found (%s)", h.module, h.func, h.desc ? h.desc : "?");
                fail++;
            }
        } else {
            if (!g_minhookInited) {
                if (MH_Initialize() != MH_OK) {
                    JY_LOGE("hook", "MinHook init failed");
                    return ok;
                }
                g_minhookInited = true;
            }
            MH_STATUS st = MH_CreateHookApi(h.module, h.func, h.proxy, h.original);
            if (st == MH_OK) {
                JY_LOGI("hook", "MinHook %ws!%s -> %s", h.module, h.func, h.desc ? h.desc : "?");
                ok++;
            } else {
                JY_LOGW("hook", "MinHook %ws!%s failed (status=%d)", h.module, h.func, (int)st);
                fail++;
            }
        }
    }

    if (g_minhookInited && ok > 0) {
        MH_EnableHook(nullptr);
    }
    JY_LOGI("hook", "install summary: %d OK, %d FAIL", ok, fail);
    return ok;
}
