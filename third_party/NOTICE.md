# Third-party components

## AntdUI

The application embeds the unmodified .NET Framework 4.6 assembly from
AntdUI 2.4.11, Copyright (c) Tom 2024-2030, licensed under Apache-2.0.
Upstream: https://github.com/AntdUI/AntdUI. Package:
https://www.nuget.org/packages/AntdUI/2.4.11. The NuGet package SHA-256 is
`21b856ffa3ec518a0576492fac9c529b7436fead02e6b057af9aa9c0c917b908`.
`scripts/restore-ui.ps1` verifies the archive before every build; binaries are
cached locally, not committed. The GUI uses its buttons, panel, checkbox,
language selector and popover. No separate UI runtime installation is needed.

AntdUI includes SVG.NET rendering code, Copyright (c) svg-net, under the
Microsoft Public License: https://github.com/svg-net/SVG. The Apache-2.0 and
Ms-PL license texts are retained in this directory and included in the EXE's
third-party-notice export, portable release notice and installed notice.

## Inno Setup Chinese translation

`installer/Languages/ChineseSimplified.isl` is an unmodified copy from
https://github.com/jrsoftware/issrc at commit
`6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2`. It is maintained upstream by
Zhenghan Yang (Kira). The Inno Setup license and copyright notices are retained
in `installer/Languages/LICENSE.Inno-Setup.txt`; the installer retains Inno
Setup's built-in copyright and website notices.

## Lucide icons

The application icon uses the unmodified `battery-medium` paths from
https://github.com/lucide-icons/lucide/blob/main/icons/battery-medium.svg,
placed on a rounded green background in `src/BatteryChargeMeter.svg`.
The interface also uses Lucide `pin` and `settings-2` paths with a neutral
stroke. The complete Lucide/Feather license text is retained in
`LICENSE.Lucide.txt` and included in the same notice export.

ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

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
| Source commit | `c683032770575d7705d1149f9d7fa7fd381766fc` |
| Source bundle | `PawnIO.Modules-0.2.10-source.zip` |
| Source bundle SHA-256 | `afb96a3d6f562350d3cd0b0af1ca3dc5c3d53ff6dc4d28b15d23f015b2a4030d` |

The file is redistributed unmodified and is embedded into the executable as a
managed resource so that the portable application remains self-contained.
Placing a file named `IntelMSR.bin` beside the executable overrides the embedded
copy, which is how the LGPL requirement to allow replacing the library is
satisfied.

Rebuilding it from source needs the PawnIO compiler toolchain from the upstream
project. The corresponding upstream tag archive is vendored beside the binary;
it includes `IntelMSR.p`, the Pawn headers, the compiler source/binary packages,
the upstream build workflow, and the complete LGPL-2.1 text. The compiled
artefact is vendored here only so that a normal build does not need that
toolchain or network access.

Every GitHub Release publishes the source bundle and a standalone third-party
notice beside the portable executable and installer. The installer also places
that notice and the LGPL-2.1 text beside the installed application. The same
notice and licence are embedded in the EXE and can be extracted with
`--third-party-notices <path>`.

## PawnIO driver and PawnIOLib

Not redistributed. The driver is installed separately by the user from
https://pawnio.eu/ and `PawnIOLib.dll` is loaded from that installation.

PawnIO itself is GPL-2.0 with an exception for independent modules that
communicate with it solely through its device IO control interface, which is
what `PawnIOLib` does on this program's behalf. `PawnIOLib` is
LGPL-2.1-or-later and is dynamically loaded, never statically linked.
