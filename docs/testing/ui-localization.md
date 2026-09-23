# Compact UI, localization and product rename

Power Meter is displayed as 功率计 in Simplified Chinese. The executable and
release assets use `PowerMeter`; the repository URL, settings key, installer
AppId and logon-task ownership identity retain their existing values for
compatibility. No release version is changed by this work.

The normal client area is 420 × 448 logical pixels. Four aligned name/value
rows replace the clipped columns. A diagnostic expands the client height to
508; clearing it removes both its space and scrollbar. Text, chart and battery
bar share 20-pixel side insets. The icon uses Lucide battery-medium geometry.

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

On 2026-09-23, this passed on Windows with Inno Setup 6.7.3, including:

- Power derivation, signed battery supplementation, measured/estimated display,
  per-monitor DPI transitions, CLI routing and embedded third-party notices.
- Chinese → English → Chinese switching at 96, 144, 168, 192, 288 and back to
  96 DPI. Each pass covers idle, charging, battery discharge, external-power
  supplementation and unavailable platform power. Actual WinForms controls
  are checked for overlapping bounds and clipped labels. Diagnostic expansion
  and collapse are exercised repeatedly at each scale.
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

The installer regression fixture uses a short unique AppId: Inno Setup shortens
long IDs in uninstall registry keys, so a test must not infer an unshortened
key from an oversized ID.

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
