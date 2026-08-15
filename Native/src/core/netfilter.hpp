#pragma once
#include <windows.h>

// Actively clear TDNetFilter.sys web-filtering state. Compiled into BOTH
// bypass_main.dll (StudentMain) and bypass_master.dll (MasterHelper) -
// the latter runs as SYSTEM and is the only one that can open the driver.
//
// Sends:
//   0x120004 (0x208 B, all zero) -> clears the 128-DWORD whitelist table,
//            local proxy port (0x7f30) and the filter-enable flag (0x7f34);
//            with 0x7f34==0 the WFP classify callback never redirects
//   0x120018 (0x98 B, all zero) -> mode = 0 (no filtering)
//   0x120010 / 0x120014         -> clears whitelist/blacklist process lists
void ClearNetFilter();
