#pragma once
#include <windows.h>

// ============================================================================
// TDNetFilter driver control (network simulation bypass, MasterHelper side).
//
// The teacher console's "network simulation" feature drives the tdnetfilter
// driver through a device IOCTL protocol. Sending the same IOCTLs with
// all-zero payloads switches the driver back to "no filtering" mode:
//   1) main switch: 128-DWORD table + proxy port (0x7f30) + enable flag (0x7f34)
//   2) mode = 0 (no filtering)
//   3) clear whitelist/blacklist process rule lists
// ============================================================================

// Disable the driver's filtering entirely (idempotent; no-op if the
// device is not present).
void ClearNetFilter();
