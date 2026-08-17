# Battery Charge Meter agent notes

## Project shape

- This is a Windows WinForms monitor targeting .NET Framework 4.7.
- Portable EXE and per-user installer are equal release options. Do not turn
  "single-file" back into a hard product requirement.

## Read before changing

- Read `README.md` for user-visible behavior, packaging, and release flow.
- Read `CONTEXT.md` before changing power names, boundaries, formulas, or UI
  labels.
- Read `third_party/NOTICE.md` before changing PawnIO integration, the embedded
  IntelMSR module, or release assets.

## Non-negotiable behavior

- Battery terminal power is signed: charging is positive and discharge is
  negative. On external power the estimate is Platform Power plus that signed
  value; never clamp battery supplementation to zero.
- On battery, System Load Power is measured as the magnitude of battery
  discharge. On external power, System Input Power remains an estimate because
  conversion losses are not measured.
- The application runs `asInvoker`. PawnIO/Psys is optional, requires a
  user-installed official driver and an elevated launch, and must never be
  installed silently by this application.
- Preserve the replaceable `IntelMSR.bin` sidecar path and the release notice,
  licence, and exact corresponding-source bundle required by its LGPL terms.

## Verification and release

- Run `pwsh -NoProfile -File .\scripts\test.ps1` from a Windows-local checkout.
  When starting in WSL, copy the checkout to a Windows-local temporary path;
  the .NET Framework compiler rejects WSL UNC paths.
- Put a candidate manifest version on `master`, then wait for CI on that exact
  SHA to pass before creating its matching annotated release tag. Remote tags
  are immutable; a tagged failure is fixed in the next patch, never by moving
  or reusing the tag.
- A release tag `vX.Y.Z` must match manifest version `X.Y.Z.0`.
- The Release workflow must publish both packaging formats, both SHA-256 files,
  the third-party notice, and the pinned PawnIO corresponding-source bundle.
- `dist/` is generated and ignored; release binaries belong in GitHub Releases,
  not in the repository.
