[CmdletBinding()]
param(
    [switch]$RequireInstaller,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distDir = Join-Path $repoRoot 'dist'
if (-not $ReportPath) { $ReportPath = Join-Path $distDir 'test-report.json' }
$started = Get-Date
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$phaseStarted = $stopwatch.Elapsed
$phase = 'source-and-preparation'
$checks = [ordered]@{
    'source-and-preparation' = 'not-run'
    'build' = 'not-run'
    'portable-executable' = 'not-run'
    'release-packaging-inputs' = 'not-run'
    'real-installer-install-uninstall' = 'not-run'
    'power-self-test' = 'not-run'
    'autostart' = 'not-run'
    'ui' = 'not-run'
}
$durations = [ordered]@{}
$installerSkipReason = $null
$failure = $null
$installerCompilerVersion = $null
$installerCompilerPath = $null
$realInstallerCompilerPath = $null
function Complete-Phase([string]$NextPhase) {
    $script:checks[$script:phase] = 'passed'
    $script:durations[$script:phase] = [math]::Round(($script:stopwatch.Elapsed - $script:phaseStarted).TotalSeconds, 3)
    $script:phase = $NextPhase
    $script:phaseStarted = $script:stopwatch.Elapsed
}
try {
$manifestPath = Join-Path $repoRoot 'src\BatteryChargeMeter.manifest'
$releaseWorkflowPath = Join-Path $repoRoot '.github\workflows\release.yml'
$installerScriptPath = Join-Path $repoRoot 'installer\BatteryChargeMeter.iss'
$modulePath = Join-Path $repoRoot 'third_party\IntelMSR.bin'
$licensePath = Join-Path $repoRoot 'third_party\LICENSE.LGPL-2.1.txt'
$sourceBundlePath = Join-Path $repoRoot 'third_party\PawnIO.Modules-0.2.10-source.zip'

if (-not (Test-Path -LiteralPath $licensePath)) {
    throw "Missing bundled LGPL-2.1 license: $licensePath"
}
if (-not (Test-Path -LiteralPath $sourceBundlePath)) {
    throw "Missing corresponding PawnIO.Modules source bundle: $sourceBundlePath"
}
$expectedModuleHash = 'd6ed85d65ab17a22f813ef98207d6d537155ee2ded5976a21cb48413c9b92e5f'
$expectedLicenseHash = 'dc626520dcd53a22f727af3ee42c770e56c97a64fe3adb063799d8ab032fe551'
$expectedSourceBundleHash = 'afb96a3d6f562350d3cd0b0af1ca3dc5c3d53ff6dc4d28b15d23f015b2a4030d'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $modulePath).Hash.ToLowerInvariant() -ne $expectedModuleHash) {
    throw 'The vendored IntelMSR.bin does not match the pinned PawnIO.Modules 0.2.10 module.'
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $licensePath).Hash.ToLowerInvariant() -ne $expectedLicenseHash) {
    throw 'The vendored LGPL-2.1 text does not match the pinned upstream copy.'
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourceBundlePath).Hash.ToLowerInvariant() -ne $expectedSourceBundleHash) {
    throw 'The vendored PawnIO.Modules source bundle does not match the pinned 0.2.10 archive.'
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$executionLevel = $manifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level
if ($executionLevel -ne 'asInvoker') {
    throw "Expected the portable app to run asInvoker; found: $executionLevel"
}

$releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
foreach ($requiredFragment in @(
    "-Formats 'Portable', 'Installer'",
    'installer=$($package.InstallerPath)',
    'installer_checksum=$($package.InstallerChecksumPath)',
    '${{ steps.package.outputs.installer }}#Windows installer',
    '${{ steps.package.outputs.installer_checksum }}#Installer SHA-256 checksum'
)) {
    if (-not $releaseWorkflow.Contains($requiredFragment)) {
        throw "Release workflow does not publish the selectable installer format: $requiredFragment"
    }
}

$installerScript = Get-Content -LiteralPath $installerScriptPath -Raw
foreach ($requiredFragment in @(
    'PrivilegesRequired=lowest',
    'DestName: "PowerMeter.exe"',
    'DestName: "THIRD-PARTY-NOTICES.txt"',
    'DestName: "LICENSE.LGPL-2.1.txt"'
)) {
    if (-not $installerScript.Contains($requiredFragment)) {
        throw "Installer definition is missing a required package contract: $requiredFragment"
    }
}

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir | Out-Null
Set-Content -LiteralPath (Join-Path $distDir 'stale-build-output.txt') -Value 'stale'

Complete-Phase 'build'
& (Join-Path $PSScriptRoot 'build.ps1')

$artifacts = @(
    Get-ChildItem -LiteralPath $distDir -File |
        Sort-Object -Property Name
)

$executableArtifacts = @($artifacts | Where-Object Name -eq 'PowerMeter.exe')
if ($executableArtifacts.Count -ne 1) {
    throw 'The build must produce PowerMeter.exe.'
}
if (Test-Path -LiteralPath (Join-Path $distDir 'stale-build-output.txt')) {
    throw 'The build did not clean stale output.'
}
$executableArtifact = $executableArtifacts[0]

$metadataProbe = @'
$assembly = [Reflection.Assembly]::LoadFile($env:BATTERY_CHARGE_METER_TEST_ASSEMBLY)
$targetFramework = $assembly.GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute' } |
    Select-Object -First 1
if ($targetFramework) {
    $targetFramework.ConstructorArguments[0].Value
}
'@
$previousAssemblyPath = $env:BATTERY_CHARGE_METER_TEST_ASSEMBLY
try {
    $env:BATTERY_CHARGE_METER_TEST_ASSEMBLY = $executableArtifact.FullName
    $powerShellPath = (Get-Process -Id $PID).Path
    $targetFrameworkName = & $powerShellPath -NoProfile -Command $metadataProbe
    if ($LASTEXITCODE -ne 0) {
        throw "Target framework metadata probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:BATTERY_CHARGE_METER_TEST_ASSEMBLY = $previousAssemblyPath
}
if ($targetFrameworkName -ne '.NETFramework,Version=v4.7') {
    throw "Expected .NET Framework 4.7 assembly metadata; found: $targetFrameworkName"
}

Complete-Phase 'portable-executable'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) (
    'battery-charge-meter-test-' + [Guid]::NewGuid().ToString('N')
)
$dpiProcess = $null
$dpiReturnProcess = $null
$noticeProcess = $null
$embeddedNoticesBytes = $null

try {
    New-Item -ItemType Directory -Path $probeDir | Out-Null
    $probeExe = Join-Path $probeDir 'PowerMeter.exe'
    $previewPath = Join-Path $probeDir 'tray-preview.png'
    Copy-Item -LiteralPath $executableArtifact.FullName -Destination $probeExe

    $embeddedNoticesPath = Join-Path $probeDir 'third-party-notices.txt'
    $noticeProcess = Start-Process `
        -FilePath $probeExe `
        -ArgumentList @('--third-party-notices', ('"{0}"' -f $embeddedNoticesPath)) `
        -PassThru

    if (-not $noticeProcess.WaitForExit(5000)) {
        $noticeProcess.Kill()
        $noticeProcess.WaitForExit()
        throw 'Embedded third-party notice extraction did not exit.'
    }
    if ($noticeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $embeddedNoticesPath)) {
        throw 'Embedded third-party notice extraction failed.'
    }

    $embeddedNotices = Get-Content -LiteralPath $embeddedNoticesPath -Raw
    $embeddedNoticesBytes = [IO.File]::ReadAllBytes($embeddedNoticesPath)
    if ($embeddedNotices -notmatch 'GNU LESSER GENERAL PUBLIC LICENSE' -or
        $embeddedNotices -notmatch 'PawnIO.Modules-0.2.10-source.zip') {
        throw 'Embedded third-party notices are incomplete.'
    }

    # Invalid CLI arguments must exit, never fall through to the GUI/UAC path.
    foreach ($invalidArguments in @(
        '--unknown', '--self-test', '--power-probe', '--third-party-notices',
        '--screenshot', '--dpi-preview', '--tray-preview', '--no-elevate extra'
    )) {
        $invalidProcess = Start-Process -FilePath $probeExe -ArgumentList $invalidArguments -PassThru
        if (-not $invalidProcess.WaitForExit(5000)) {
            $invalidProcess.Kill()
            $invalidProcess.WaitForExit()
            throw "Invalid CLI arguments opened a persistent process: $invalidArguments"
        }
        if ($invalidProcess.ExitCode -ne 2) {
            throw "Invalid CLI arguments returned $($invalidProcess.ExitCode): $invalidArguments"
        }
    }

    $powerReportPath = Join-Path $probeDir 'power-probe.txt'
    $powerProcess = Start-Process -FilePath $probeExe `
        -ArgumentList @('--power-probe', ('"{0}"' -f $powerReportPath), '1') -PassThru
    if (-not $powerProcess.WaitForExit(20000)) {
        $powerProcess.Kill()
        $powerProcess.WaitForExit()
        throw 'Power probe did not finish without interactive elevation.'
    }
    if ($powerProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $powerReportPath)) {
        throw 'Power probe did not create its report.'
    }
    $powerReport = Get-Content -LiteralPath $powerReportPath -Raw
    foreach ($field in @('Elevated token:', 'timestamp=', 'elapsed=', 'supply=', 'BMS internal timestamp: unknown')) {
        if (-not $powerReport.Contains($field)) {
            throw "Power probe omitted diagnostic field: $field"
        }
    }

    $screenPath = Join-Path $probeDir 'window-preview.png'
    $screenProcess = Start-Process -FilePath $probeExe `
        -ArgumentList @('--screenshot', ('"{0}"' -f $screenPath)) -PassThru
    if (-not $screenProcess.WaitForExit(10000)) {
        $screenProcess.Kill()
        $screenProcess.WaitForExit()
        throw 'Window preview did not exit without interactive elevation.'
    }
    if ($screenProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $screenPath)) {
        throw 'Window preview did not create its image.'
    }

    $process = Start-Process `
        -FilePath $probeExe `
        -ArgumentList @('--tray-preview', ('"{0}"' -f $previewPath), '42', 'charging') `
        -Wait `
        -PassThru

    if ($process.ExitCode -ne 0) {
        throw "Portable EXE smoke test failed with exit code $($process.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $previewPath)) {
        throw 'Portable EXE smoke test did not create the tray preview.'
    }

    $dpiPreviewPath = Join-Path $probeDir 'dpi-preview.png'
    $dpiProcess = Start-Process `
        -FilePath $probeExe `
        -ArgumentList @('--dpi-preview', ('"{0}"' -f $dpiPreviewPath), '192') `
        -PassThru

    if (-not $dpiProcess.WaitForExit(5000)) {
        $dpiProcess.Kill()
        $dpiProcess.WaitForExit()
        throw 'DPI transition preview did not exit after handling WM_DPICHANGED.'
    }
    if ($dpiProcess.ExitCode -ne 0) {
        throw "DPI transition preview failed with exit code $($dpiProcess.ExitCode)."
    }

    $dpiMetadataPath = "$dpiPreviewPath.txt"
    if (-not (Test-Path -LiteralPath $dpiPreviewPath) -or
        -not (Test-Path -LiteralPath $dpiMetadataPath)) {
        throw 'DPI transition preview did not create its image and metadata.'
    }

    $dpiMetadata = Get-Content -LiteralPath $dpiMetadataPath -Raw
    if ($dpiMetadata -notmatch 'HandledDpi=192(?=;|$)' -or
        $dpiMetadata -notmatch 'Content=768x(?:936|1056)(?=;|$)' -or
        $dpiMetadata -notmatch 'FormFontPixels=24(?:\.0+)?(?=;|$)' -or
        $dpiMetadata -notmatch 'PowerFontPixels=117\.333(?=;|$)' -or
        $dpiMetadata -notmatch 'ErrorArea=688x96(?=;|$)' -or
        $dpiMetadata -notmatch 'AutoScroll=True(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionApplied=True(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionMatched=True(?=;|$)') {
        throw "Unexpected DPI transition metadata: $dpiMetadata"
    }
    if ($dpiMetadata -notmatch 'Client=(?<width>\d+)x(?<height>\d+)(?=;|$)') {
        throw "Missing DPI viewport metadata: $dpiMetadata"
    }
    if ([int]$Matches.width -gt 768 -or [int]$Matches.height -gt 1056) {
        throw "DPI viewport exceeds its scrollable content: $dpiMetadata"
    }

    $dpiReturnPreviewPath = Join-Path $probeDir 'dpi-return-preview.png'
    $dpiReturnProcess = Start-Process `
        -FilePath $probeExe `
        -ArgumentList @(
            '--dpi-preview',
            ('"{0}"' -f $dpiReturnPreviewPath),
            '192',
            '96'
        ) `
        -PassThru

    if (-not $dpiReturnProcess.WaitForExit(5000)) {
        $dpiReturnProcess.Kill()
        $dpiReturnProcess.WaitForExit()
        throw 'DPI return preview did not exit after two WM_DPICHANGED messages.'
    }
    if ($dpiReturnProcess.ExitCode -ne 0) {
        throw "DPI return preview failed with exit code $($dpiReturnProcess.ExitCode)."
    }

    $dpiReturnMetadataPath = "$dpiReturnPreviewPath.txt"
    if (-not (Test-Path -LiteralPath $dpiReturnPreviewPath) -or
        -not (Test-Path -LiteralPath $dpiReturnMetadataPath)) {
        throw 'DPI return preview did not create its image and metadata.'
    }

    $dpiReturnMetadata = Get-Content -LiteralPath $dpiReturnMetadataPath -Raw
    if ($dpiReturnMetadata -notmatch 'HandledDpi=96(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'Content=384x(?:468|528)(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'FormFontPixels=12(?:\.0+)?(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'PowerFontPixels=58\.667(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'ErrorArea=344x48(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'WindowPositionApplied=True(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'WindowPositionMatched=True(?=;|$)') {
        throw "Unexpected DPI return metadata: $dpiReturnMetadata"
    }
}
finally {
    if ($dpiProcess -and -not $dpiProcess.HasExited) {
        $dpiProcess.Kill()
        $dpiProcess.WaitForExit()
    }
    if ($dpiReturnProcess -and -not $dpiReturnProcess.HasExited) {
        $dpiReturnProcess.Kill()
        $dpiReturnProcess.WaitForExit()
    }
    if ($noticeProcess -and -not $noticeProcess.HasExited) {
        $noticeProcess.Kill()
        $noticeProcess.WaitForExit()
    }
    if (Test-Path -LiteralPath $probeDir) {
        Remove-Item -LiteralPath $probeDir -Recurse -Force
    }
}

Complete-Phase 'release-packaging-inputs'
$packageDir = Join-Path ([IO.Path]::GetTempPath()) (
    'battery-charge-meter-package-test-' + [Guid]::NewGuid().ToString('N')
)

try {
    New-Item -ItemType Directory -Path $packageDir | Out-Null
    $installerCompilerPath = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    $usingRealInstallerCompiler = [bool]$installerCompilerPath
    $realInstallerCompilerPath = $installerCompilerPath
    if ($usingRealInstallerCompiler) {
        # ISCC.exe's Windows version resource can be 0.0.0.0. Query its own
        # preprocessor instead, so a Chocolatey shim cannot supply the version.
        $versionProbePath = Join-Path $packageDir 'compiler-version.iss'
        @'
#pragma message "ISCC_VERSION=" + VersionToStr(Ver)
[Setup]
AppName=CompilerVersionProbe
AppVersion=1
DefaultDirName={autopf}\CompilerVersionProbe
'@ | Set-Content -LiteralPath $versionProbePath -Encoding utf8
        $versionOutput = (& $installerCompilerPath '/O-' $versionProbePath 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch 'ISCC_VERSION=(?<version>\d+\.\d+\.\d+\.\d+)') {
            throw "Inno Setup compiler version probe failed: $versionOutput"
        }
        $installerCompilerVersion = $Matches.version
        Write-Host "Installer verification: real Inno Setup compiler ($installerCompilerPath), including install/uninstall."
    } else {
        $installerSkipReason = 'Inno Setup ISCC.exe is unavailable; real installer creation and per-user install/uninstall were not executed.'
        if ($RequireInstaller) { throw $installerSkipReason }
        Write-Warning $installerSkipReason
    }

    if (-not $installerCompilerPath) {
        $installerCompilerPath = Join-Path $packageDir 'fake-iscc.ps1'
        @'
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CompilerArguments
)
$outputDirectory = $null
$outputBaseName = $null
foreach ($argument in $CompilerArguments) {
    if ($argument -like '/DOutputDir=*') {
        $outputDirectory = $argument.Substring('/DOutputDir='.Length)
    }
    elseif ($argument -like '/DOutputBaseFilename=*') {
        $outputBaseName = $argument.Substring('/DOutputBaseFilename='.Length)
    }
}
if (-not $outputDirectory -or -not $outputBaseName) {
    throw 'Missing installer output definitions.'
}
Set-Content -LiteralPath (Join-Path $outputDirectory "$outputBaseName.exe") -Value 'fake installer'
'@ | Set-Content -LiteralPath $installerCompilerPath -Encoding utf8
    }

    $package = & (Join-Path $PSScriptRoot 'package-release.ps1') `
        -Tag 'v9.8.7' `
        -ExecutablePath $executableArtifact.FullName `
        -OutputDirectory $packageDir `
        -Formats @('Portable', 'Installer') `
        -InstallerCompilerPath $installerCompilerPath

    $expectedBinaryName = 'PowerMeter-v9.8.7-windows.exe'
    $expectedChecksumName = "$expectedBinaryName.sha256"
    $expectedInstallerName = 'PowerMeter-v9.8.7-windows-setup.exe'
    $expectedInstallerChecksumName = "$expectedInstallerName.sha256"
    $expectedNoticesName = 'PowerMeter-THIRD-PARTY-NOTICES.txt'
    $expectedSourceName = 'PawnIO.Modules-0.2.10-source.zip'
    if ((Split-Path -Leaf $package.BinaryPath) -ne $expectedBinaryName) {
        throw "Unexpected Release binary name: $($package.BinaryPath)"
    }
    if ((Split-Path -Leaf $package.ChecksumPath) -ne $expectedChecksumName) {
        throw "Unexpected Release checksum name: $($package.ChecksumPath)"
    }
    if ((Split-Path -Leaf $package.InstallerPath) -ne $expectedInstallerName) {
        throw "Unexpected installer name: $($package.InstallerPath)"
    }
    if ((Split-Path -Leaf $package.InstallerChecksumPath) -ne $expectedInstallerChecksumName) {
        throw "Unexpected installer checksum name: $($package.InstallerChecksumPath)"
    }
    if ((Split-Path -Leaf $package.NoticesPath) -ne $expectedNoticesName) {
        throw "Unexpected third-party notices name: $($package.NoticesPath)"
    }
    if ((Split-Path -Leaf $package.SourceBundlePath) -ne $expectedSourceName) {
        throw "Unexpected third-party source bundle name: $($package.SourceBundlePath)"
    }
    if (-not (Test-Path -LiteralPath $package.NoticesPath) -or
        -not (Test-Path -LiteralPath $package.SourceBundlePath)) {
        throw 'Release compliance assets were not created.'
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $package.SourceBundlePath).Hash.ToLowerInvariant() -ne
        $expectedSourceBundleHash) {
        throw 'Release source bundle hash differs from the pinned upstream archive.'
    }

    $releaseNotices = Get-Content -LiteralPath $package.NoticesPath -Raw
    if ($releaseNotices -notmatch 'GNU LESSER GENERAL PUBLIC LICENSE' -or
        $releaseNotices -notmatch 'PawnIO.Modules-0.2.10-source.zip') {
        throw 'Release third-party notices are incomplete.'
    }
    $releaseNoticesBytes = [IO.File]::ReadAllBytes($package.NoticesPath)
    if ([Convert]::ToBase64String($releaseNoticesBytes) -ne
        [Convert]::ToBase64String($embeddedNoticesBytes)) {
        throw 'Release notices differ from the application notice export.'
    }

    $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.BinaryPath).Hash.ToLowerInvariant()
    $actualChecksum = (Get-Content -LiteralPath $package.ChecksumPath -Raw).Trim()
    if ($actualChecksum -ne "$expectedHash  $expectedBinaryName") {
        throw "Unexpected Release checksum content: $actualChecksum"
    }
    $expectedInstallerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.InstallerPath).Hash.ToLowerInvariant()
    $actualInstallerChecksum = (Get-Content -LiteralPath $package.InstallerChecksumPath -Raw).Trim()
    if ($actualInstallerChecksum -ne "$expectedInstallerHash  $expectedInstallerName") {
        throw "Unexpected installer checksum content: $actualInstallerChecksum"
    }

    $installerOnlyDir = Join-Path $packageDir 'installer-only'
    $installerOnly = & (Join-Path $PSScriptRoot 'package-release.ps1') `
        -Tag 'v9.8.7' `
        -ExecutablePath $executableArtifact.FullName `
        -OutputDirectory $installerOnlyDir `
        -Formats 'Installer' `
        -InstallerCompilerPath $installerCompilerPath
    if ($installerOnly.BinaryPath -or $installerOnly.ChecksumPath -or
        -not (Test-Path -LiteralPath $installerOnly.InstallerPath)) {
        throw 'Installer-only packaging did not honor the selected Release format.'
    }

    $portableOnlyDir = Join-Path $packageDir 'portable-only'
    $portableOnly = & (Join-Path $PSScriptRoot 'package-release.ps1') `
        -Tag 'v9.8.7' `
        -ExecutablePath $executableArtifact.FullName `
        -OutputDirectory $portableOnlyDir `
        -Formats 'Portable'
    if ($portableOnly.InstallerPath -or $portableOnly.InstallerChecksumPath -or
        -not (Test-Path -LiteralPath $portableOnly.BinaryPath)) {
        throw 'Portable-only packaging did not honor the selected Release format.'
    }

    Complete-Phase 'real-installer-install-uninstall'
    if ($usingRealInstallerCompiler) {
        & (Join-Path $PSScriptRoot 'test-installer-uninstall.ps1') `
            -InstallerCompilerPath $installerCompilerPath `
            -ExecutablePath $executableArtifact.FullName
        Complete-Phase 'power-self-test'
    } else {
        $checks['real-installer-install-uninstall'] = 'not-run'
        $durations['real-installer-install-uninstall'] = 0
        $phase = 'power-self-test'
        $phaseStarted = $stopwatch.Elapsed
    }
}
finally {
    if (Test-Path -LiteralPath $packageDir) {
        Remove-Item -LiteralPath $packageDir -Recurse -Force
    }
}

# Power derivation across supply states and firmware rate combinations. These
# paths hand the user a wrong number without any sensor misbehaving, and no
# single machine can be put into all of them on demand, so they are checked
# mechanically rather than by whatever state this machine happens to be in.
$selfTestDir = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $selfTestDir | Out-Null
try {
    $selfTestPath = Join-Path $selfTestDir 'self-test.txt'
    $selfTestProcess = Start-Process -FilePath $executableArtifact.FullName `
        -ArgumentList '--self-test', ('"{0}"' -f $selfTestPath) -PassThru
    if (-not $selfTestProcess.WaitForExit(15000)) {
        $selfTestProcess.Kill()
        $selfTestProcess.WaitForExit()
        throw 'Power self test did not exit within 15 seconds.'
    }

    if (-not (Test-Path -LiteralPath $selfTestPath)) {
        throw 'Power self test did not produce a report.'
    }

    $selfTestReport = Get-Content -LiteralPath $selfTestPath -Raw
    Write-Host $selfTestReport

    if ($selfTestProcess.ExitCode -ne 0 -or $selfTestReport -notmatch 'Power self test passed\.') {
        throw "Power self test failed with exit code $($selfTestProcess.ExitCode)."
    }
}
finally {
    Remove-Item -LiteralPath $selfTestDir -Recurse -Force -ErrorAction SilentlyContinue
}

Complete-Phase 'autostart'
& (Join-Path $PSScriptRoot 'test-autostart.ps1') -Executable $executableArtifact.FullName -GuiOnly

Complete-Phase 'ui'
& (Join-Path $PSScriptRoot 'test-ui.ps1') -Executable $executableArtifact.FullName

Complete-Phase ''
if ($installerSkipReason) {
    Write-Warning 'Application checks and packaging-input checks passed; real installer acceptance was not executed.'
} else {
    Write-Host 'Application and Release package tests passed, including real installer install/uninstall.'
}
}
catch {
    $failure = $_.Exception.Message
    if ($phase -and $checks[$phase] -eq 'not-run') {
        $checks[$phase] = 'failed'
        $durations[$phase] = [math]::Round(($stopwatch.Elapsed - $phaseStarted).TotalSeconds, 3)
    }
    throw
}
finally {
    $stopwatch.Stop()
    $commit = (& git -C $repoRoot rev-parse HEAD 2>$null | Select-Object -First 1)
    $frameworkRelease = $null
    try { $frameworkRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch {}
    $frameworkVersion = if ($frameworkRelease -ge 533320) { '4.8.1' }
        elseif ($frameworkRelease -ge 528040) { '4.8' }
        elseif ($frameworkRelease -ge 461808) { '4.7.2' }
        elseif ($frameworkRelease -ge 461308) { '4.7.1' }
        elseif ($frameworkRelease -ge 460798) { '4.7' }
        else { 'unknown' }
    $preparationSeconds = [double]$durations['source-and-preparation']
    $buildSeconds = [double]$durations['build']
    $report = [ordered]@{
        commit = $commit
        started_at = $started.ToString('o')
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        powershell = $PSVersionTable.PSVersion.ToString()
        dotnet_framework_version = $frameworkVersion
        dotnet_framework_release = $frameworkRelease
        target_framework = '.NETFramework,Version=v4.7'
        inno_setup = [ordered]@{ path = $realInstallerCompilerPath; version = $installerCompilerVersion }
        scope = if ($RequireInstaller) { 'strict-real-installer' } else { 'local-optional-installer' }
        result = if ($failure) { 'failed' } elseif ($installerSkipReason) { 'partial-installer-not-run' } else { 'passed' }
        checks = $checks
        phase_seconds = $durations
        preparation_seconds = $preparationSeconds
        build_seconds = $buildSeconds
        test_seconds = [math]::Round(($stopwatch.Elapsed.TotalSeconds - $preparationSeconds - $buildSeconds), 3)
        total_seconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        installer_not_run_reason = $installerSkipReason
        failure = $failure
    }
    $reportDir = Split-Path -Parent $ReportPath
    if ($reportDir) { New-Item -ItemType Directory -Path $reportDir -Force | Out-Null }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Host "Verification report: $ReportPath"
}
