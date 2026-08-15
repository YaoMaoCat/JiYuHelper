#pragma once
#include <windows.h>

// Walk the IAT of a module and patch a specific function import.
// Returns the original function address, or nullptr if not found.
void* PatchIat(HMODULE hModule, const char* importDllName,
               const char* funcName, void* proxyFunc);
