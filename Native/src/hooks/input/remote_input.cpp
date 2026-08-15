#include "remote_input.h"
#include "../../common/hook/hook.h"
#include "../../common/log/log.h"
#include <vector>

typedef UINT (WINAPI* SendInput_t)(UINT, LPINPUT, int);
static SendInput_t Real_SendInput = nullptr;

typedef BOOL (WINAPI* BlockInput_t)(BOOL);
static BlockInput_t Real_BlockInput = nullptr;

// Runtime switch (hot-update); default OFF, enabled via HotUpdate()
static bool g_enabled = false;

void SetRemoteEnabled(bool enable) {
    g_enabled = enable;
}

// ---- SendInput ----
// Swallow simulated input entirely and report success.

static UINT WINAPI ProxySendInput(UINT count, LPINPUT inputs, int cbSize) {
    if (!g_enabled) return Real_SendInput(count, inputs, cbSize);
    for (UINT i = 0; i < count; i++) {
        if (inputs[i].type == INPUT_MOUSE) {
            JY_LOGT(3000, "remote", "SendInput mouse swallowed (flags=0x%04X)", inputs[i].mi.dwFlags);
        } else if (inputs[i].type == INPUT_KEYBOARD) {
            JY_LOGT(3000, "remote", "SendInput kb swallowed (vk=0x%04X)", inputs[i].ki.wVk);
        }
    }
    return count; // return success without sending
}

// ---- BlockInput ----
// BlockInput(TRUE) disables ALL mouse/keyboard input system-wide.
// JiYu calls this during remote control / monitoring to lock student input.
// We intercept and log it, but return success without actually blocking.

static BOOL WINAPI ProxyBlockInput(BOOL fBlock) {
    if (!g_enabled) return Real_BlockInput(fBlock);
    if (fBlock) {
        JY_LOGI("remote", "BlockInput(TRUE) intercepted - local input kept alive");
        return TRUE; // lie: pretend we blocked
    }
    // unblock requests pass through
    if (Real_BlockInput) return Real_BlockInput(fBlock);
    return TRUE;
}

void InstallRemoteInputHook() {
    std::vector<HookEntry> hooks = {
        { HookType::MinHook, L"user32", nullptr, "SendInput",
          ProxySendInput, (void**)&Real_SendInput, "remote input" },
        { HookType::MinHook, L"user32", nullptr, "BlockInput",
          ProxyBlockInput, (void**)&Real_BlockInput, "block input" },
    };
    InstallHooks(hooks);
    JY_LOGI("remote", "SendInput + BlockInput intercepted - teacher remote control disabled");
}
