#include "pe.h"
#include "../log/log.h"

void* PatchIat(HMODULE hMod, const char* importDll, const char* funcName, void* proxy) {
    BYTE* base = (BYTE*)hMod;
    PIMAGE_DOS_HEADER dos = (PIMAGE_DOS_HEADER)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;

    PIMAGE_NT_HEADERS nt = (PIMAGE_NT_HEADERS)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;

    DWORD rva = (DWORD)nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    DWORD sz  = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].Size;
    if (sz == 0) return nullptr;

    PIMAGE_IMPORT_DESCRIPTOR desc = (PIMAGE_IMPORT_DESCRIPTOR)(base + rva);
    for (; desc->Name; desc++) {
        const char* mod = (const char*)(base + desc->Name);
        if (_stricmp(mod, importDll) != 0) continue;

        PIMAGE_THUNK_DATA thunk = (PIMAGE_THUNK_DATA)(base + desc->FirstThunk);
        for (; thunk->u1.AddressOfData; thunk++) {
            if (IMAGE_SNAP_BY_ORDINAL(thunk->u1.Ordinal)) continue;
            auto ibn = (PIMAGE_IMPORT_BY_NAME)(base + thunk->u1.AddressOfData);
            if (strcmp((char*)ibn->Name, funcName) != 0) continue;

            void* orig = (void*)thunk->u1.Function;
            DWORD old;
            VirtualProtect(&thunk->u1.Function, 4, PAGE_READWRITE, &old);
            thunk->u1.Function = (ULONG_PTR)proxy;
            VirtualProtect(&thunk->u1.Function, 4, old, &old);
            JY_LOGD("pe", "IAT %s!%s patched", importDll, funcName);
            return orig;
        }
    }
    JY_LOGD("pe", "IAT %s!%s not found", importDll, funcName);
    return nullptr;
}
