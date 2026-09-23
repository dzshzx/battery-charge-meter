# Physical laptop acceptance

The automated Windows-local suite exercises the portable executable, package
assets, and an isolated per-user installer lifecycle. A laptop with a real
battery and an optional user-installed PawnIO driver needs separate observation.
Record these checks against the exact candidate commit and built executable;
pending checks do not count as passed.

| Check | Steps and expected result | Actual result and evidence |
| --- | --- | --- |
| Identity | Record `git rev-parse HEAD`, Windows build (`winver`), PowerShell version, .NET Framework 4.x Release registry value, Inno Setup version, and SHA-256 of the tested EXE/setup. Match the report's commit and tool versions. | Pending: record host, date, hashes, and report path. |
| Battery operation | On battery power, run `PowerMeter.exe --power-probe report.txt 10` and observe the GUI. The battery terminal value is negative, System Load Power is its positive magnitude, and the display does not label it as measured adapter input. Save a redacted probe report and screenshot. | Pending: real battery host evidence. |
| External power | Connect AC and repeat the probe. Charging shows positive battery terminal power; supplementation, if observed, shows negative battery terminal power. Estimated System Input Power uses platform plus signed battery power and is marked estimated. Do not infer unobserved supplementation from another state. | Pending: real AC transition evidence. |
| Ordinary launch | Launch without elevation or cancel the GUI's UAC prompt. Battery and CPU measurements remain available when supported; platform/Psys explains unavailability. CLI diagnostics must not show UAC. | Pending: ordinary-token and UAC evidence. |
| Driver and admin launch | Only if the user has installed the official PawnIO driver, launch the GUI as administrator and run `--power-probe`. Record driver and hardware availability; Psys may still be unavailable on unsupported wiring. Do not install a driver as part of this check. | Pending: driver-equipped administrator evidence. |
| Installed application | Install the exact setup under the current user, check the installed EXE, Start Menu entry, notices and LGPL text, then uninstall. Record the installed EXE SHA-256 and cleanup outcome. | Pending: physical-host install/uninstall receipt. |

Use `dist/test-report.json` for automated pass/fail/not-run results. Add the
actual host observations beside this checklist in a dated test receipt before
claiming physical-laptop acceptance.
