# Compact UI, localization and product rename

Power Meter is displayed as 功率计 in Simplified Chinese. The executable and
release assets use `PowerMeter`; the repository URL, settings key, installer
AppId and logon-task ownership identity retain their existing values for
compatibility.

The normal client area is 384 × 468 logical pixels. A centered 44-point reading
leads, with the boundary caption above, followed by the supply state and battery
electrical details below.
Mean and peak form a balanced pair. A soft tonal group holds the battery level
and the three other power boundaries, so all four remain available without
duplicating the headline. A diagnostic expands the client height to 528;
clearing it removes both its space and scrollbar. Display mode is at the top;
the footer holds the timestamp, a pin toggle and Settings. Startup,
language and elevation controls live in the settings popover. The icon uses
Lucide battery-medium geometry; pin and settings-2 are used inside the window.

Language defaults to the Windows UI language (Chinese locales use Simplified
Chinese, other locales use English). The window and tray language menus offer
system default, Chinese and English. Explicit choices persist in the existing
HKCU settings key. Cached hardware diagnostics are translated at presentation
time, leaving OS exception details and user paths intact.

## Verification

Run from a Windows-local checkout:

```powershell
pwsh -NoProfile -File scripts/test.ps1
```

Local Windows verification and packaging run on NERV, from a Windows-local
checkout using PowerShell 7. GitHub Windows CI is an additional check.
On 2026-09-23, verification included:

- Power derivation, signed battery supplementation, measured/estimated display,
  per-monitor DPI transitions, CLI routing and embedded third-party notices.
- Chinese → English → Chinese switching at 96, 144, 168, 192, 288 and back to
  96 DPI. Each pass covers idle, charging, battery discharge, external-power
  supplementation and unavailable platform power. Actual WinForms controls
  are checked for overlapping bounds and clipped labels. Diagnostic expansion
  and collapse are exercised repeatedly at each scale.
- Startup with one sample and a three-second warm-up window are rendered too.
  Full statistics use concise captions; partial windows retain their actual
  coverage. Each mode exposes exactly three supporting rows. The tests exercise
  mode changes, error-area expansion and DPI changes with this row layout.
- The unselected mode segment gains a quiet hover fill and text emphasis.
  Bitmap comparisons verify the feedback appears, disappears on mouse leave,
  and never changes the selected mode by itself.
- Actual pin clicks toggle the window's TopMost state in both directions; its
  glyph must render in both states. The settings popover opens in each language
  and scale, and its content is checked for clipping, overlap and double scaling.
  A DPI transition disposes the popover before replacing the fonts.
- Real English install, Chinese upgrade, localized uninstall names, and
  upgrade replacement of the old executable with the compatibility launcher.
  An isolated legacy logon task is recognized by the new executable and, since
  2026-09-26, migrated to the protected copy; a different portable copy cannot
  claim it. See `protected-autostart.md`.
- The actual legacy launcher starts the new GUI hidden with `--autostart`,
  retaining the inherited token. A manual new-name launch restores that same
  process. Cleanup forwarding preserves the child exit code.
- Real uninstall cancellation, cleanup failure and successful removal. The
  tests use unique installer/task identities and temporary directories.
- All 14 Python version-authorization tests.

`scripts/test-ui.ps1` also runs separately and saves PNGs plus layout metadata
under `dist/ui-preview/`. These use deterministic fixture measurements, not
live laptop readings. The 175% Chinese and English idle previews were visually
reviewed, along with unavailable-source and small-icon renders.

The initial installer acceptance used real Inno Setup 6.7.3. The installer
regression fixture uses a short unique AppId: Inno Setup shortens
long IDs in uninstall registry keys, so a test must not infer an unshortened
key from an oversized ID.

Text alignment is checked from rendered glyphs: the two statistic values must
agree within one physical pixel. The large numeric readout and smaller W unit
use font ascent metrics and are checked for a common rendered baseline. The
settings controls use AntdUI's button, checkbox and selector rendering.

The fixture host adopts the application's Per-Monitor V2 thread context before
creating controls. Screenshots use the visible DWM frame, excluding invisible
resize margins that PrintWindow otherwise leaves black. This keeps the native
title bar and control rendering faithful at the host monitor's actual DPI.

## Visual references and toolkit choice

The official screenshots of [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray)
and [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) informed the
compact utility layout: prominent live values, coherent groups, quiet surfaces,
and secondary operations collected into settings. The Windows
[type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
informed regular labels, semibold numeric values and a smaller watt unit.

Further reference review covered [Stats](https://github.com/exelban/stats),
[BatteryBoi](https://github.com/thebarbican19/BatteryBoi), and
[EnergyStarX](https://github.com/JasonWei512/EnergyStarX). Stats' battery panel
separates one primary figure from supporting measurements; BatteryBoi focuses a
small surface on one state and value; EnergyStarX keeps Windows-native controls
and a clear central focus. The resulting meter centers the watts reading,
reduces repeated data and uses one quiet supporting group. No normalization ring
is used for watts because the app has no measured maximum input capacity.
On Windows 11 the headline uses Segoe UI Variable Display; other systems fall
back to Segoe UI. The watt unit is aligned by font ascent rather than line-box
height, and estimates retain a neutral muted color and the approximation mark.

[AntdUI](https://github.com/AntdUI/AntdUI) and
[Krypton Toolkit](https://github.com/Krypton-Suite/Standard-Toolkit) were checked
for existing .NET Framework support. AntdUI supplies the specific buttons,
popover, panel and settings controls without replacing the WinForms lifecycle.
Its pinned 2.4.11 net46 package has no additional NuGet runtime dependencies.
The assembly is embedded and resolved before the GUI entry point is JIT-compiled;
the existing isolated-EXE CLI/preview tests verify that no DLL sidecar is needed.
The restore script checks the archive hash and re-extracts the assembly on each
build. The executable is approximately 3.3 MiB with the UI toolkit and notices.

Lucide stroke geometry is wrapped in a group because AntdUI applies a root fill
when tinting SVGs. Both states of the pin explicitly carry the same glyph.
Settings bounds are already scaled by the application, so automatic content
scaling inside the popover is disabled to avoid applying DPI twice.

## Upgrade compatibility

A fresh installation contains `PowerMeter.exe`. When an old
`BatteryChargeMeter.exe` exists in the same installation directory, setup
replaces it with the small forwarding launcher. The launcher accepts GUI,
ordinary GUI, logon startup and startup cleanup entry points; diagnostic CLI
commands use `PowerMeter.exe`. Existing elevated task actions and ACLs need no
rewrite during a per-user upgrade. Uninstall removes the matching legacy task
through the new executable before deleting either executable.

Portable files that are moved or renamed need startup enabled again from the
new location, with the existing replacement confirmation. Keeping the old
launcher is an installation compatibility path, not a portable alias rule.
