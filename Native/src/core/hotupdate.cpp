#include "hotupdate.hpp"
#include "../hook/keyboard.hpp"
#include "../hook/topmost.hpp"
#include "../hook/focuslock.hpp"
#include "../hook/applist.hpp"
#include "../hook/proclist.hpp"
#include "../hook/procguard.hpp"
#include "../hook/screen.hpp"
#include "../hook/screencap.hpp"
#include "../hook/remote.hpp"
#include "../hook/tdmaster.hpp"
#include "../hook/filterguard.hpp"
#include "../hook/netfilterguard.hpp"
#include "../monitor/monitor.hpp"

void HotUpdate(uint64_t mask) {
    SetKeyboardEnabled(mask & FEATURE_KEYBOARD);
    SetTopmostEnabled(mask & FEATURE_TOPMOST);
    SetFocusLockEnabled(mask & FEATURE_FOCUS);
    SetAppListEnabled(mask & FEATURE_APPLIST);
    SetProcListEnabled(mask & FEATURE_PROCLIST);
    SetProcGuardEnabled(mask & FEATURE_PROCGUARD);
    SetScreenFakeEnabled(mask & FEATURE_SCREENFAKE);
    SetScreenCapEnabled(mask & FEATURE_SCREENCAP);
    SetRemoteEnabled(mask & FEATURE_REMOTE);
    SetTDMasterEnabled(mask & FEATURE_INPUTLOCK);
    SetFilterGuardEnabled(mask & FEATURE_FILTER);
    SetNetFilterEnabled(mask & FEATURE_NETSIM);
    SetMonitorEnabled(mask & FEATURE_BLACKMON);
}
