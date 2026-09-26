# Protected logon startup

The logon task runs with the highest available token. It must never execute a
file that the signed-in user's ordinary token can replace, because a replaced
file would then start elevated at the next sign-in without a UAC prompt.

## Invariants

- The task action is always
  `C:\Program Files\Power Meter\autostart\<SID>\PowerMeter.exe`. That folder,
  `autostart` and `Power Meter` carry an explicit, non-inherited DACL (SYSTEM
  and Administrators full control, Users read and execute) and are owned by
  Administrators, as are the copied files.
- Only an elevated process writes the copy, and it copies only its own image
  plus an `IntelMSR.bin` beside it. A process running from a protected copy
  never copies from its recorded source (`source.txt`).
- A task that still runs a user-writable file (older versions, including the
  `BatteryChargeMeter.exe` compatibility launcher) is never reported as working
  startup. Setup migrates it with one UAC prompt or deletes it when elevation
  is declined; an elevated launch of the new version also migrates it.
- A confirmation to replace another installation's startup covers the task
  definition and the copy's recorded owner.
- The task ACL is unchanged: the ordinary token may read, run and delete it,
  but not rewrite its action.

## Automated coverage

- `--self-test`: task inspection with the protected path, recorded owner,
  unprotected-task reporting, identity mapping, and CLI routing for
  `--sync-autostart`.
- `scripts/test-installer-uninstall.ps1` (inside `scripts/test.ps1`, requires
  an elevated token): real Inno install, upgrade over a legacy task that runs
  the user-writable launcher (migrated to the protected copy), ACL and owner
  checks on every protected level, replacement of the installed executable,
  upgrade refresh to a different executable with an unchanged task, and
  uninstall cancel / cleanup failure / success, which removes task, copy and
  the empty folders. It uses a unique `Program Files\Power Meter InstallerTest
  <id>` root and task name.
- `scripts/test-autostart.ps1 -CheckOrdinaryClient` (manual, elevated
  interactive session): migration and report-only exit codes, replacement of
  the user copy through the real Explorer token, write/rename/delete/ACL
  attempts on the protected copy from that token, an elevated scheduler run
  of the original copy afterwards, restore of the protected instance by a
  manual launch of its source, and removal of an unused copy.

The UAC prompt raised by an unelevated setup or uninstaller cannot be answered
by automation, so that branch (and its declined fallback) is covered only by
code review.

## Host acceptance, 2026-09-26

On NERV (Windows 11 26200, Inno Setup 6.7.1) against candidate commit
`582b540`, with the real AppId, task name and protected root. Setup binaries
were built from two builds of that commit (v9.9.1 and v9.9.2 test tags). Steps
ran from an elevated session; ordinary-token steps ran at medium integrity in
the interactive session.

| Step | Observed |
| --- | --- |
| Install released v1.4.0 (ordinary token) and enable startup with its own code | Task ran `%LOCALAPPDATA%\Programs\Battery Charge Meter\BatteryChargeMeter.exe`, a folder where the user has full control. |
| Upgrade to candidate build 1 | Task action and working folder moved to the protected copy; its SHA-256 matched the installed EXE; the old name became the legacy launcher. All three folders: owner Administrators, protected DACL `SYSTEM:F, Administrators:F, Users:RX`. |
| Ordinary token | `--sync-autostart` returned 0. Replacing both user-folder EXEs succeeded. Opening the copy for write, creating a file beside it, renaming or deleting it, renaming its folder, adding an ACE and rewriting the task action were all denied. |
| Run the real task on demand | The process ran from the protected path, elevated, with the original build 1 hash, not the replacement. |
| Ordinary token places build 2 in the install folder | `--sync-autostart` returned 3; the copy was unchanged. |
| Upgrade to build 2 while the logon instance ran | Copy hash became build 2; the task definition was byte-identical; the instance was stopped and restarted from the refreshed copy, elevated. |
| Uninstall | Task, copy and `Program Files\Power Meter` removed; install folder, uninstall key and processes gone. |
| Fresh candidate install with a legacy-shaped task, then scheduler start | The elevated new version migrated its own task to the protected copy at start. Ordinary `--remove-autostart` then returned 3 (task deleted, copy kept); elevated `--sync-autostart` removed the unused copy and folders. |

Sign-out and sign-in were not performed; the scheduler's on-demand run uses
the same principal and action as the logon trigger.
