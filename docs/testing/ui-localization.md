# Compact UI, localization and product rename

Power Meter is displayed as 功率计 in Simplified Chinese. The executable and
release assets use `PowerMeter`; the repository URL, settings key, installer
AppId and logon-task ownership identity retain their existing values for
compatibility. No release version is changed by this work.

The normal client area is 420 × 468 logical pixels. The main reading and trend
lead, mean and peak have separate captions and values, and a single rounded
group holds the battery level and four aligned power-source rows. A diagnostic
expands the client height to 528; clearing it removes both its space and
scrollbar. The footer holds display mode, a pin toggle and Settings. Startup,
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
- Actual pin clicks toggle the window's TopMost state in both directions; its
  glyph must render in both states. The settings popover opens in each language
  and scale, and its content is checked for clipping, overlap and double scaling.
  A DPI transition disposes the popover before replacing the fonts.
- Real English install, Chinese upgrade, localized uninstall names, and
  upgrade replacement of the old executable with the compatibility launcher.
  An isolated legacy logon task remains unchanged and is recognized by the new
  executable; a different portable copy cannot claim it.
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

Text alignment is also checked from rendered glyphs: the electrical detail and
timestamp render the same probe string in their actual controls, and their ink
positions must agree within one physical pixel at every tested DPI. The settings
controls use AntdUI's button, checkbox and selector rendering.

## Visual references and toolkit choice

The official screenshots of [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray)
and [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) informed the
compact utility layout: prominent live values, coherent groups, quiet surfaces,
and secondary operations collected into settings. The Windows
[type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
informed regular labels, semibold numeric values and a smaller watt unit.

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
