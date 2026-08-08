[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$noticeSource = Join-Path $repoRoot 'third_party\NOTICE.md'
$licenseSource = Join-Path $repoRoot 'third_party\LICENSE.LGPL-2.1.txt'
$sourceBundleSource = Join-Path $repoRoot 'third_party\PawnIO.Modules-0.2.10-source.zip'

foreach ($requiredPath in @($noticeSource, $licenseSource, $sourceBundleSource)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Missing required third-party distribution file: $requiredPath"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$binaryName = "BatteryChargeMeter-$Tag-windows.exe"
$binaryPath = Join-Path $resolvedOutputDirectory $binaryName
$checksumPath = "$binaryPath.sha256"
$noticesPath = Join-Path $resolvedOutputDirectory 'BatteryChargeMeter-THIRD-PARTY-NOTICES.txt'
$sourceBundlePath = Join-Path $resolvedOutputDirectory 'PawnIO.Modules-0.2.10-source.zip'

Copy-Item -LiteralPath $resolvedExecutable -Destination $binaryPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $binaryPath).Hash.ToLowerInvariant()
"$hash  $binaryName" | Set-Content -LiteralPath $checksumPath -Encoding ascii

$noticeText = @(
    Get-Content -LiteralPath $noticeSource -Raw
    "`n---`nGNU Lesser General Public License 2.1`n---`n"
    Get-Content -LiteralPath $licenseSource -Raw
) -join "`n"
Set-Content -LiteralPath $noticesPath -Value $noticeText -Encoding utf8
Copy-Item -LiteralPath $sourceBundleSource -Destination $sourceBundlePath -Force

[pscustomobject]@{
    BinaryPath = $binaryPath
    ChecksumPath = $checksumPath
    NoticesPath = $noticesPath
    SourceBundlePath = $sourceBundlePath
}
