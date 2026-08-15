cmake_minimum_required(VERSION 3.20)

# Disable cl.exe's /showIncludes dependency scanning. On a Chinese-locale MSVC,
# CMake writes the localized prefix to rules.ninja in GBK while ninja reads it
# as UTF-8, so the match fails and "note: including file: ..." lines leak into
# the build log. Use CMake's source-based scanning (#include directives) instead.
# Trade-off: after editing a header you may need "Rebuild All" rather than incremental.
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
# MasterHelper 32-bit on JiYu v6.0; x64 branch kept for completeness):
#   x86 build  -> jyhelper_main.dll   (injected into StudentMain.exe)
#   x64 build  -> jyhelper_master.dll (injected into MasterHelper.exe)
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

# ---- jyhelper_main.dll (StudentMain.exe) ----
# All source lives under src/: common/ (shared layers), hooks/ (feature
# modules by domain), main/ (entry). JiYu v6.0 is 32-bit ONLY.
if(CMAKE_SIZEOF_VOID_P EQUAL 4)
    add_library(bypass_main SHARED
        src/main/dllmain.cpp
        src/common/log/log.cpp
        src/common/pipe/pipe.cpp
        src/common/hook/hook.cpp
        src/common/config/config.cpp
        src/common/util/pe.cpp
        src/hooks/input/keyboard.cpp
        src/hooks/input/remote_input.cpp
        src/hooks/window/topmost.cpp
        src/hooks/window/focuslock.cpp
        src/hooks/window/subclass.cpp
        src/hooks/visibility/applist.cpp
        src/hooks/visibility/proclist.cpp
        src/hooks/screen/screen_fake.cpp
        src/hooks/screen/screencap.cpp
        src/hooks/screen/blackmonitor.cpp
        src/hooks/proc/procguard.cpp
        src/hooks/device/filterguard.cpp
        src/hooks/network/netsim_guard.cpp
        src/hooks/td/tdmaster.cpp
    )

    target_link_libraries(bypass_main PRIVATE ${MINHOOK_TARGET} user32 gdiplus psapi)
    set_target_properties(bypass_main PROPERTIES OUTPUT_NAME jyhelper_main)
endif()

# ---- jyhelper_master.dll (MasterHelper.exe) ----
add_library(bypass_master SHARED
    src/main/dllmaster.cpp
    src/master/guard.cpp
    src/common/log/log.cpp
    src/common/pipe/pipe.cpp
    src/common/hook/hook.cpp
    src/common/config/config.cpp
    src/common/util/pe.cpp
    src/common/util/netfilter.cpp
)

target_link_libraries(bypass_master PRIVATE ${MINHOOK_TARGET} user32)
set_target_properties(bypass_master PROPERTIES OUTPUT_NAME jyhelper_master)

# ---- injector helper: REMOVED ----
# Injection is now done entirely from the x64 JiYuHelper app:
#   64-bit targets -> standard CreateRemoteThread
#   32-bit targets -> wow64 NtCreateThreadEx + 32-bit kernel32!LoadLibraryW
# (see JiYuHelper Core/ProcessManager.cs)
