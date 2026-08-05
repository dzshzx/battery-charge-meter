[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distDir = Join-Path $repoRoot 'dist'

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

if ($artifacts.Count -ne 1 -or $artifacts[0].Name -ne 'BatteryChargeMeter.exe') {
    $actualNames = ($artifacts | ForEach-Object Name) -join ', '
    throw "Expected one standalone BatteryChargeMeter.exe artifact; found: $actualNames"
}

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
    $env:BATTERY_CHARGE_METER_TEST_ASSEMBLY = $artifacts[0].FullName
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

try {
    New-Item -ItemType Directory -Path $probeDir | Out-Null
    $probeExe = Join-Path $probeDir 'BatteryChargeMeter.exe'
    $previewPath = Join-Path $probeDir 'tray-preview.png'
    Copy-Item -LiteralPath $artifacts[0].FullName -Destination $probeExe

    $process = Start-Process `
        -FilePath $probeExe `
        -ArgumentList @('--tray-preview', ('"{0}"' -f $previewPath), '42', 'charging') `
        -Wait `
        -PassThru

    if ($process.ExitCode -ne 0) {
        throw "Standalone EXE smoke test failed with exit code $($process.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $previewPath)) {
        throw 'Standalone EXE smoke test did not create the tray preview.'
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
        $dpiMetadata -notmatch 'Client=860x1000(?=;|$)' -or
        $dpiMetadata -notmatch 'FormFontPixels=24(?:\.0+)?(?=;|$)' -or
        $dpiMetadata -notmatch 'PowerFontPixels=112(?:\.0+)?(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionApplied=True(?=;|$)' -or
        $dpiMetadata -notmatch 'WindowPositionMatched=True(?=;|$)') {
        throw "Unexpected DPI transition metadata: $dpiMetadata"
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
        $dpiReturnMetadata -notmatch 'Client=430x500(?=;|$)' -or
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
    if (Test-Path -LiteralPath $probeDir) {
        Remove-Item -LiteralPath $probeDir -Recurse -Force
    }
}

$packageDir = Join-Path ([IO.Path]::GetTempPath()) (
    'battery-charge-meter-package-test-' + [Guid]::NewGuid().ToString('N')
)

try {
    New-Item -ItemType Directory -Path $packageDir | Out-Null
    $package = & (Join-Path $PSScriptRoot 'package-release.ps1') `
        -Tag 'v9.8.7' `
        -ExecutablePath $artifacts[0].FullName `
        -OutputDirectory $packageDir

    $expectedBinaryName = 'BatteryChargeMeter-v9.8.7-windows.exe'
    $expectedChecksumName = "$expectedBinaryName.sha256"
    if ((Split-Path -Leaf $package.BinaryPath) -ne $expectedBinaryName) {
        throw "Unexpected Release binary name: $($package.BinaryPath)"
    }
    if ((Split-Path -Leaf $package.ChecksumPath) -ne $expectedChecksumName) {
        throw "Unexpected Release checksum name: $($package.ChecksumPath)"
    }

    $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.BinaryPath).Hash.ToLowerInvariant()
    $actualChecksum = (Get-Content -LiteralPath $package.ChecksumPath -Raw).Trim()
    if ($actualChecksum -ne "$expectedHash  $expectedBinaryName") {
        throw "Unexpected Release checksum content: $actualChecksum"
    }

    $archives = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.zip' -File)
    if ($archives.Count -ne 0) {
        throw 'Release package must not require a ZIP archive.'
    }
}
finally {
    if (Test-Path -LiteralPath $packageDir) {
        Remove-Item -LiteralPath $packageDir -Recurse -Force
    }
}

Write-Host 'Standalone EXE and Release package tests passed.'
