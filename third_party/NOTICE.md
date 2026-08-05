# Third-party components

## IntelMSR.bin

A PawnIO module that exposes a whitelisted set of Intel MSRs, including
`MSR_PLATFORM_ENERGY_STATUS` (`0x64D`). This program uses it to read platform
power; see the "平台功率" section of the README.

| | |
|---|---|
| Upstream | https://github.com/namazso/PawnIO.Modules |
| Release | `0.2.10`, published 2026-07-27 |
| Asset | `release_0_2_10.zip` |
| File | `IntelMSR.bin` |
| SHA-256 | `d6ed85d65ab17a22f813ef98207d6d537155ee2ded5976a21cb48413c9b92e5f` |
| Licence | LGPL-2.1-or-later |
| Source | `IntelMSR.p` in the upstream repository at the tag above |

The file is redistributed unmodified and is embedded into the executable as a
managed resource so that the program stays a single file. Placing a file named
`IntelMSR.bin` beside the executable overrides the embedded copy, which is how
the LGPL requirement to allow replacing the library is satisfied.

Rebuilding it from source needs the PawnIO compiler toolchain from the upstream
project; the compiled artefact is vendored here only so that a normal build of
this repository does not need that toolchain or network access.

## PawnIO driver and PawnIOLib

Not redistributed. The driver is installed separately by the user from
https://pawnio.eu/ and `PawnIOLib.dll` is loaded from that installation.

PawnIO itself is GPL-2.0 with an exception for independent modules that
communicate with it solely through its device IO control interface, which is
what `PawnIOLib` does on this program's behalf. `PawnIOLib` is
LGPL-2.1-or-later and is dynamically loaded, never statically linked.
