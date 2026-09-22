# Power display, GUI elevation and login startup acceptance

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
- Portable/installer packaging. With ISCC installed, an isolated installer
  identity uses the shipping uninstall hook and task manager. Real confirmation
  dialogs exercise cancellation, cleanup failure and successful removal against
  a disabled test task. Cancellation preserves the task, cleanup failure keeps
  the installation, and successful removal deletes the matching task. Without
  ISCC, the suite checks packaging inputs with a fake compiler.
- Task policy and ownership, stale replacement confirmation, and detecting
  changed battery/idle/network/timeout/instance settings. The GUI-only autostart
  integration launches a temporary copy under the existing token, verifies its
  hidden window, duplicate suppression and restoration by a manual launch.

For full Task Scheduler integration, run `scripts/test-autostart.ps1` with an
already elevated token in the current interactive Windows session. It registers
only a unique `BatteryChargeMeter.Test.<guid>` task against temporary EXE copies
and removes those resources afterward. It exercises actual task launch, elevated
token readback, replacement, drift repair, duplicate suppression and removal.
Add `-CheckOrdinaryClient` to exercise window restoration and task deletion via
the ordinary Explorer token. It uses `test-autostart-ordinary-client.ps1`, whose
arguments are restricted to the matching isolated test task and temporary
directory. This option requires an interactive Explorer desktop for the same
Windows user.

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
7. Enable login startup from the development copy using a disposable test
   account. Verify the registered executable path, user, interactive logon and
   elevated run level. Start the task explicitly: the app must enter the tray
   without another UAC dialog, and another scheduled start must not create a
   duplicate instance. Battery operation must not prevent or stop the task.
8. Disable login startup and read back that its task is gone. A moved portable
   copy must not silently adopt another copy's registration; uninstall cleanup
   must preserve registrations targeting other executable paths. Keep actual
   reboot/logon acceptance distinct from a manual scheduled-task launch.

## Development verification, 2026-09-22

The Windows-local full test and 14 Python tests passed. This local run used the
fake installer compiler; real install/uninstall runs when Inno Setup is available.
The test log identifies which packaging path was exercised.
A separate UI event check exercised selector/tray clicks, synchronized checkmarks,
history reset and loading the preference in another form instance. The check
restored the original preference afterward. 100%, 175% and 300% fixture previews
were generated, and the 175% error-area scaling defect was fixed and covered by
the automated DPI check.

A development EXE was copied separately to the REDMI Book Pro 14 2025 and run with
`--power-probe` using an existing elevated session. EMI and PawnIO were available;
five samples reported 27.98 W battery charge, 31.80–46.08 W platform and
59.78–74.06 W estimated input. These samples verify the data path and unchanged
formula, not absolute input-meter accuracy.

The user confirmed on this notebook that canceling the first UAC prompt kept the
ordinary window usable, then the administrator restart action succeeded after
approval, with matching whole-system window and tray readings. Process readback
showed one development process with the elevation-attempted argument. A
simultaneous physical-meter comparison is not part of this automated evidence.

The autostart increment passed the Windows-local suite (89 self-test cases and
the ordinary-token GUI integration). On the notebook, the full isolated
`test-autostart.ps1 -CheckOrdinaryClient` run passed registration, replacement,
policy drift/repair, actual elevated scheduler launch, hidden-window restoration,
duplicate suppression, handoff and deletion. The Explorer-launched helper
confirmed its ordinary token and the same user SID before restoring the elevated
window and deleting the task. Windows account-name normalization and the task
ACL needed for ordinary-token cleanup were corrected using these live results.
Independent review found no remaining issues in those fixes. These checks did
not restart or log off the notebook; actual login-trigger acceptance remains a
separate manual check. The installed release and its startup state were retained.

The uninstall lifecycle regression passed 18 assertions with real Inno Setup
6.7.1. Cleanup runs after affirmative confirmation and before file removal;
failures abort removal while keeping the EXE and uninstall data. The fixture
uses unique task, installer and shortcut identities and cleans them afterward.
