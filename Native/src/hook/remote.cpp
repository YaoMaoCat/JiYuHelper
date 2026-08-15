#include "remote.hpp"
#include "engine.hpp"
#include "../core/log.hpp"

// Hook user32!SendInput to block simulated mouse/keyboard from teacher control.
// Hook user32!BlockInput to prevent JiYu from disabling ALL local input.

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

static UINT WINAPI ProxySendInput(UINT count, LPINPUT inputs, int cbSize) {
    if (!g_enabled) return Real_SendInput(count, inputs, cbSize);
    for (UINT i = 0; i < count; i++) {
        if (inputs[i].type == INPUT_MOUSE) {
            LogThrottled(3000, "[Remote] SendInput mouse blocked (flags=0x%04X)", inputs[i].mi.dwFlags);
        } else if (inputs[i].type == INPUT_KEYBOARD) {
            LogThrottled(3000, "[Remote] SendInput kb blocked (vk=0x%04X)", inputs[i].ki.wVk);
        }
    }
    return count; // Return success without sending
}

// ---- BlockInput ----
// BlockInput(TRUE) disables ALL mouse/keyboard input system-wide.
// JiYu calls this during remote control / monitoring to lock student input.
// We intercept and log it, but return success without actually blocking.

static BOOL WINAPI ProxyBlockInput(BOOL fBlock) {
    if (!g_enabled) return Real_BlockInput(fBlock);
    if (fBlock) {
        Log("[Remote] Blocked BlockInput(TRUE) - input kept alive");
        return TRUE; // Lie: pretend we blocked
    }
    // Unblock requests pass through
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
    Log("[Remote] SendInput+BlockInput blocked - teacher remote control disabled");
}
