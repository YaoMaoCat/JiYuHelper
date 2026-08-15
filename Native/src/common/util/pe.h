#pragma once
#include <windows.h>

// ============================================================================
// PE utilities.
// ============================================================================

// Rewrite the IAT slot of importDll!funcName inside hMod so it points to
// proxy. Returns the original function pointer (nullptr if not found).
// Caller keeps the returned pointer as the "original" for chaining.
void* PatchIat(HMODULE hMod, const char* importDll, const char* funcName, void* proxy);
