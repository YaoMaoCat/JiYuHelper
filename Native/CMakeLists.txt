cmake_minimum_required(VERSION 3.20)

# Disable cl.exe's /showIncludes dependency scanning. On a Chinese-locale MSVC,
# CMake writes the localized prefix to rules.ninja in GBK while ninja reads it
# as UTF-8, so the match fails and "note: including file: ..." lines leak into
# the build log. Use CMake's source-based scanning (#include directives) instead.
# Trade-off: after editing a .hpp you may need "Rebuild All" rather than incremental.
set(CMAKE_DEPENDS_USE_COMPILER OFF)

project(JiYuHook LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Static CRT (/MT) so no DLL dependencies on target machine
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# Source files are UTF-8; without this the GBK compiler mis-parses
# non-ASCII comments (a multi-byte char can swallow the newline and merge
# the next code line into the comment). This caused BypassStart to silently
# lose its ClearNetFilter() call.
add_compile_options(/utf-8)

# ============================================================================
# Architecture split (real-world JiYu install: StudentMain 32-bit,
# MasterHelper 64-bit):
#   x86 build  -> bypass_main.dll  (injected into StudentMain.exe)
#              + inject32.exe      (32-bit injector helper)
#   x64 build  -> bypass_master.dll (injected into MasterHelper.exe)
#              + inject64.exe      (64-bit injector helper)
# ============================================================================

# ---- MinHook ----
# x64: local prebuilt static lib (libMinHook.x64.lib, put by user)
# x86: local prebuilt static lib (libMinHook.x86.lib)
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(MINHOOK_LIB_PATH "${CMAKE_SOURCE_DIR}/thirdparty/MinHook/lib/libMinHook.x64.lib")
else()
    set(MINHOOK_LIB_PATH "${CMAKE_SOURCE_DIR}/thirdparty/MinHook/lib/libMinHook.x86.lib")
endif()

add_library(MinHook STATIC IMPORTED)
set_target_properties(MinHook PROPERTIES
    IMPORTED_LOCATION "${MINHOOK_LIB_PATH}"
)
target_include_directories(MinHook INTERFACE
    "${CMAKE_SOURCE_DIR}/thirdparty/MinHook/include"
)
set(MINHOOK_TARGET MinHook)

# ---- bypass_main.dll (StudentMain.exe) + bypass_master.dll (MasterHelper.exe) ----
# JiYu v6.0 is 32-bit ONLY (both processes). Both DLLs are built as x86.
if(CMAKE_SIZEOF_VOID_P EQUAL 4)
    add_library(bypass_main SHARED
        src/dllmain.cpp
        src/core/log.cpp
        src/core/pe.cpp
        src/core/pipe.cpp
        src/core/hotupdate.cpp
        src/core/netfilter.cpp
        src/hook/engine.cpp
        src/hook/keyboard.cpp
        src/hook/topmost.cpp
        src/hook/applist.cpp
        src/hook/proclist.cpp
        src/hook/screen.cpp
        src/hook/remote.cpp
        src/hook/screencap.cpp
        src/hook/tdmaster.cpp
        src/hook/focuslock.cpp
        src/hook/procguard.cpp
        src/hook/filterguard.cpp
        src/hook/netfilterguard.cpp
        src/window/subclass.cpp
        src/monitor/monitor.cpp
        config/config.cpp
    )

    target_link_libraries(bypass_main PRIVATE ${MINHOOK_TARGET} user32 gdiplus psapi)
    target_include_directories(bypass_main PRIVATE ${CMAKE_SOURCE_DIR})
    set_target_properties(bypass_main PROPERTIES OUTPUT_NAME jyhelper_main)

    add_library(bypass_master SHARED
        src/master_hook/dllmain.cpp
        src/master_hook/guard.cpp
        src/master_hook/log.cpp
        src/hook/engine.cpp
        src/core/pe.cpp
        src/core/pipe.cpp
        src/core/netfilter.cpp
        config/config.cpp
    )

    target_link_libraries(bypass_master PRIVATE ${MINHOOK_TARGET} user32)
    target_include_directories(bypass_master PRIVATE ${CMAKE_SOURCE_DIR})
    set_target_properties(bypass_master PROPERTIES OUTPUT_NAME jyhelper_master)
endif()

# ---- bypass_master.dll (MasterHelper.exe, 64-bit only) ----
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    add_library(bypass_master SHARED
        src/master_hook/dllmain.cpp
        src/master_hook/guard.cpp
        src/master_hook/log.cpp
        src/hook/engine.cpp
        src/core/pe.cpp
        src/core/pipe.cpp
        src/core/netfilter.cpp
        config/config.cpp
    )

    target_link_libraries(bypass_master PRIVATE ${MINHOOK_TARGET} user32)
    target_include_directories(bypass_master PRIVATE ${CMAKE_SOURCE_DIR})
    set_target_properties(bypass_master PROPERTIES OUTPUT_NAME jyhelper_master)
endif()

# ---- injector helper: REMOVED ----
# Injection is now done entirely from the x64 JiYuHelper app:
#   64-bit targets -> standard CreateRemoteThread
#   32-bit targets -> wow64 NtCreateThreadEx + 32-bit kernel32!LoadLibraryW
# (see JiYuHelper Core/ProcessManager.cs)

