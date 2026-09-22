# Power display and GUI elevation acceptance

Run `pwsh -NoProfile -File .\scripts\test.ps1` from a Windows-local checkout.
Run `python -m unittest discover -s scripts/tests -p "test_*.py"` for the release
authorization checks. Neither command should display UAC.

The application self-test includes a regression at the actual window/tray
consumer: a 39.08 W charging battery and 27 W platform must display approximately
66.08 W in whole-system mode, with a `66` tray glyph. The 14.11 W CPU package is
not added again. Battery mode displays 39.08 W / `39`; unavailable platform data
clears the whole-system display instead of substituting battery power. On battery,
system load remains available without PawnIO.

## Automated coverage

- Power/supply-state derivation, sign, missing sources and nonfinite values.
- Real elapsed-time weighting, 30-second average, 60-second peak, chart expiry,
  missing intervals and reset after a supply/mode change or long sampling gap.
- Startup routing with injected elevation success, cancellation and failure;
  already-elevated and opt-out launches; malformed CLI arguments exit with code 2.
- Real CLI execution with timeouts, diagnostic fields, notice extraction,
  screenshots, tray previews and DPI transitions. Error text bounds are checked
  to prevent TextBox AutoSize from breaking scaling.
- Portable/installer packaging. With ISCC installed, the test installs to a
  temporary directory and uninstalls; otherwise it checks packaging inputs with a
  fake compiler. Run this on a disposable test account, not one with an installed
  production copy sharing the application's installer identity.

## Interactive acceptance

Use a separately copied development EXE; keep the installed release intact.

1. Open without arguments under an ordinary token. Cancel UAC. Confirm a usable
   ordinary window remains, the reason is visible, and battery/CPU readings are
   retained. On external power, unavailable platform data means `N/A` / `--` for
   the whole-system display.
2. Select the administrator restart action. Cancel once, then accept. Verify
   cancellation preserves the window, successful handoff leaves one development
   window/tray, and the new process has an elevated token. Driver absence must
   produce an explicit reason without another elevation loop.
3. Change modes through both the selector and tray menu. Check the labels, number,
   menu checkmark, graph reset and saved mode after relaunch. When using another
   administrator's credentials, preferences belong to that Windows account.
4. Check 100%, 175% and 300% scaling and a return to 100%. Controls remain reachable
   on a constrained screen; error messages remain readable/scrollable.
5. Check charging, full/idle, unplugging, resuming from sleep and unavailable source
   recovery. A mode or supply-state transition must not retain mixed statistics.
6. If comparing a physical meter, identify its measurement boundary and record
   values over the same interval as the software. Report the average difference
   and response to load/charge changes; do not infer accuracy from nonsynchronous
   screenshots or convert smoothing into an accuracy claim.

## Development verification, 2026-09-22

The Windows-local full test and 14 Python tests passed. This local run used the
fake installer compiler; real installation is verified on the Windows CI runner.
A separate UI event check exercised selector/tray clicks, synchronized checkmarks,
history reset and loading the preference in another form instance. The check
restored the original preference afterward. 100%, 175% and 300% fixture previews
were generated, and the 175% error-area scaling defect was fixed and covered by
the automated DPI check.

A development EXE was copied separately to the REDMI Book Pro 14 2025 and run with
`--power-probe` using an existing elevated session. EMI and PawnIO were available;
five samples reported 27.98 W battery charge, 31.80–46.08 W platform and
59.78–74.06 W estimated input. These samples verify the data path and unchanged
formula, not absolute input-meter accuracy. Live interactive UAC acceptance and
simultaneous physical-meter comparison are separate from this automated evidence.
