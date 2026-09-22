# Battery Charge Meter agent notes

Windows WinForms monitor targeting .NET Framework 4.7. Power terminology and
measurement boundaries are defined in `CONTEXT.md`; packaging and release
commands are in `README.md`, and PawnIO obligations in `third_party/NOTICE.md`.

- Battery terminal power is signed: charging positive, discharge negative.
  On external power, estimated System Input Power is Platform Power plus this
  signed value, including battery supplementation. On battery, System Load
  Power is the magnitude of discharge. Conversion losses are not measured.
- Keep the manifest `asInvoker`: GUI startup requests elevation once, with
  cancellation falling back to ordinary mode. CLI commands never request UAC.
  PawnIO/Psys requires a user-installed official driver and elevated launch;
  the app must not silently install it.
- Portable EXE and per-user installer are equal release options. Preserve the
  replaceable `IntelMSR.bin` sidecar, LGPL notice/licence and exact corresponding
  source bundle; release assets include both formats and SHA-256 files.
- Verification entry: `pwsh -NoProfile -File .\scripts\test.ps1` from a
  Windows-local checkout (.NET Framework rejects WSL UNC paths). With ISCC it
  tests a real per-user install/uninstall; otherwise only packaging inputs.
- Changes use a task branch and PR. A master merge does not publish: an
  annotated `vX.Y.Z` release tag requires manifest `X.Y.Z.0` and passing CI at
  that exact SHA. Published tags are immutable; fixes use a new patch version.
- `dist/` is generated; binaries belong in GitHub Releases.
