[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateSet('Portable', 'Installer')]
    [string[]]$Formats = @('Portable'),

    [string]$InstallerCompilerPath
)

$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$licenseSource = Join-Path $repoRoot 'third_party\LICENSE.LGPL-2.1.txt'
$sourceBundleSource = Join-Path $repoRoot 'third_party\PawnIO.Modules-0.2.10-source.zip'
$installerScript = Join-Path $repoRoot 'installer\BatteryChargeMeter.iss'

$includePortable = $Formats -contains 'Portable'
$includeInstaller = $Formats -contains 'Installer'
if (-not $includePortable -and -not $includeInstaller) {
    throw 'At least one Release format is required.'
}

$requiredPaths = @($licenseSource, $sourceBundleSource)
if ($includeInstaller) {
    $requiredPaths += $installerScript
}
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Missing required third-party distribution file: $requiredPath"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$binaryName = "PowerMeter-$Tag-windows.exe"
$binaryPath = Join-Path $resolvedOutputDirectory $binaryName
$checksumPath = "$binaryPath.sha256"
$installerName = "PowerMeter-$Tag-windows-setup.exe"
$installerPath = Join-Path $resolvedOutputDirectory $installerName
$installerChecksumPath = "$installerPath.sha256"
$noticesPath = Join-Path $resolvedOutputDirectory 'PowerMeter-THIRD-PARTY-NOTICES.txt'
$sourceBundlePath = Join-Path $resolvedOutputDirectory 'PawnIO.Modules-0.2.10-source.zip'

if ($includePortable) {
    Copy-Item -LiteralPath $resolvedExecutable -Destination $binaryPath -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $binaryPath).Hash.ToLowerInvariant()
    "$hash  $binaryName" | Set-Content -LiteralPath $checksumPath -Encoding ascii
}

$noticeProcess = Start-Process `
    -FilePath $resolvedExecutable `
    -ArgumentList @('--third-party-notices', ('"{0}"' -f $noticesPath)) `
    -PassThru
if (-not $noticeProcess.WaitForExit(15000)) {
    $noticeProcess.Kill()
    $noticeProcess.WaitForExit()
    throw 'The application did not finish exporting third-party notices.'
}
if ($noticeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $noticesPath)) {
    throw "The application failed to export third-party notices (exit $($noticeProcess.ExitCode))."
}
Copy-Item -LiteralPath $sourceBundleSource -Destination $sourceBundlePath -Force

if ($includeInstaller) {
    if ($InstallerCompilerPath) {
        $resolvedInstallerCompiler = (Resolve-Path -LiteralPath $InstallerCompilerPath).Path
    }
    else {
        $compilerCandidates = @(
            (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
        $resolvedInstallerCompiler = $compilerCandidates | Select-Object -First 1
    }

    if (-not $resolvedInstallerCompiler) {
        throw 'Inno Setup 6 compiler was not found; pass -InstallerCompilerPath or select Portable only.'
    }

    $appVersion = $Tag.Substring(1)
    $outputBaseFilename = [IO.Path]::GetFileNameWithoutExtension($installerName)
    $compilerArguments = @(
        '/Q',
        "/DAppVersion=$appVersion",
        "/DSourceExe=$resolvedExecutable",
        "/DLegacyLauncher=$repoRoot\dist\compat\BatteryChargeMeter.exe",
        "/DNoticePath=$noticesPath",
        "/DLicensePath=$licenseSource",
        "/DIconPath=$repoRoot\src\BatteryChargeMeter.ico",
        "/DOutputDir=$resolvedOutputDirectory",
        "/DOutputBaseFilename=$outputBaseFilename",
        $installerScript
    )

    & $resolvedInstallerCompiler @compilerArguments
    if (-not $?) {
        throw 'Inno Setup compiler failed.'
    }
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Inno Setup did not create the expected installer: $installerPath"
    }

    $installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash.ToLowerInvariant()
    "$installerHash  $installerName" |
        Set-Content -LiteralPath $installerChecksumPath -Encoding ascii
}

[pscustomobject]@{
    BinaryPath = if ($includePortable) { $binaryPath } else { $null }
    ChecksumPath = if ($includePortable) { $checksumPath } else { $null }
    InstallerPath = if ($includeInstaller) { $installerPath } else { $null }
    InstallerChecksumPath = if ($includeInstaller) { $installerChecksumPath } else { $null }
    NoticesPath = $noticesPath
    SourceBundlePath = $sourceBundlePath
}
