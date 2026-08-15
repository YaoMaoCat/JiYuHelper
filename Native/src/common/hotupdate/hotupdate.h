#pragma once
#include <cstdint>

// ============================================================================
// Hot-update feature mask -- apply a mask at runtime.
//
// Bit layout lives in common/config/features.h (contract with the app's
// HookConfigWriter.BuildFeatureMask). The dispatcher is implemented in the
// entry module (main/dllmain.cpp for StudentMain, master/guard.cpp for
// MasterHelper), which knows all installed hook modules.
// ============================================================================

// Apply a feature mask: enable/disable each installed hook module at runtime.
// Unused bits are ignored. Must be called AFTER hooks are installed.
void HotUpdate(uint64_t mask);
