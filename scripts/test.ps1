[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distDir = Join-Path $repoRoot 'dist'
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
    'DestName: "BatteryChargeMeter.exe"',
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

& (Join-Path $PSScriptRoot 'build.ps1')

$artifacts = @(
    Get-ChildItem -LiteralPath $distDir -File |
        Sort-Object -Property Name
)

$executableArtifacts = @($artifacts | Where-Object Name -eq 'BatteryChargeMeter.exe')
if ($executableArtifacts.Count -ne 1) {
    throw 'The build must produce BatteryChargeMeter.exe.'
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

$probeDir = Join-Path ([IO.Path]::GetTempPath()) (
    'battery-charge-meter-test-' + [Guid]::NewGuid().ToString('N')
)
$dpiProcess = $null
$dpiReturnProcess = $null
$noticeProcess = $null
$embeddedNoticesBytes = $null

try {
    New-Item -ItemType Directory -Path $probeDir | Out-Null
    $probeExe = Join-Path $probeDir 'BatteryChargeMeter.exe'
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
        $dpiMetadata -notmatch 'Content=860x1000(?=;|$)' -or
        $dpiMetadata -notmatch 'FormFontPixels=24(?:\.0+)?(?=;|$)' -or
        $dpiMetadata -notmatch 'PowerFontPixels=112(?:\.0+)?(?=;|$)' -or
        $dpiMetadata -notmatch 'AutoScroll=True(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionApplied=True(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionMatched=True(?=;|$)') {
        throw "Unexpected DPI transition metadata: $dpiMetadata"
    }
    if ($dpiMetadata -notmatch 'Client=(?<width>\d+)x(?<height>\d+)(?=;|$)') {
        throw "Missing DPI viewport metadata: $dpiMetadata"
    }
    if ([int]$Matches.width -gt 860 -or [int]$Matches.height -gt 1000) {
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
        $dpiReturnMetadata -notmatch 'Content=430x500(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'FormFontPixels=12(?:\.0+)?(?=;|$)' -or
        $dpiReturnMetadata -notmatch 'PowerFontPixels=56(?:\.0+)?(?=;|$)' -or
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

$packageDir = Join-Path ([IO.Path]::GetTempPath()) (
    'battery-charge-meter-package-test-' + [Guid]::NewGuid().ToString('N')
)

try {
    New-Item -ItemType Directory -Path $packageDir | Out-Null
    $installerCompilerPath = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    $usingRealInstallerCompiler = [bool]$installerCompilerPath

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

    $expectedBinaryName = 'BatteryChargeMeter-v9.8.7-windows.exe'
    $expectedChecksumName = "$expectedBinaryName.sha256"
    $expectedInstallerName = 'BatteryChargeMeter-v9.8.7-windows-setup.exe'
    $expectedInstallerChecksumName = "$expectedInstallerName.sha256"
    $expectedNoticesName = 'BatteryChargeMeter-THIRD-PARTY-NOTICES.txt'
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

    if ($usingRealInstallerCompiler) {
        $installDir = Join-Path $packageDir 'installed-application'
        $installProcess = Start-Process `
            -FilePath $package.InstallerPath `
            -ArgumentList @(
                '/VERYSILENT',
                '/SUPPRESSMSGBOXES',
                '/NORESTART',
                ('/DIR="{0}"' -f $installDir)
            ) `
            -Wait `
            -PassThru
        if ($installProcess.ExitCode -ne 0) {
            throw "Silent installer smoke test failed with exit code $($installProcess.ExitCode)."
        }

        foreach ($installedFile in @(
            'BatteryChargeMeter.exe',
            'THIRD-PARTY-NOTICES.txt',
            'LICENSE.LGPL-2.1.txt'
        )) {
            if (-not (Test-Path -LiteralPath (Join-Path $installDir $installedFile))) {
                throw "Installer omitted required file: $installedFile"
            }
        }

        $uninstallerPath = Join-Path $installDir 'unins000.exe'
        if (-not (Test-Path -LiteralPath $uninstallerPath)) {
            throw 'Installer did not create an uninstaller.'
        }
        $uninstallProcess = Start-Process `
            -FilePath $uninstallerPath `
            -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
            -Wait `
            -PassThru
        if ($uninstallProcess.ExitCode -ne 0) {
            throw "Silent uninstaller smoke test failed with exit code $($uninstallProcess.ExitCode)."
        }
        $uninstallDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while ((Test-Path -LiteralPath $uninstallerPath) -and
            [DateTime]::UtcNow -lt $uninstallDeadline) {
            Start-Sleep -Milliseconds 100
        }
        if (Test-Path -LiteralPath $uninstallerPath) {
            throw 'Silent uninstaller did not finish removing the application.'
        }
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
    $selfTestProcess = Start-Process -FilePath $artifacts[0].FullName `
        -ArgumentList '--self-test', ('"{0}"' -f $selfTestPath) -Wait -PassThru

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

Write-Host 'Application and Release package tests passed.'
