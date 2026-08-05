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
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$binaryName = "BatteryChargeMeter-$Tag-windows.exe"
$binaryPath = Join-Path $resolvedOutputDirectory $binaryName
$checksumPath = "$binaryPath.sha256"

Copy-Item -LiteralPath $resolvedExecutable -Destination $binaryPath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $binaryPath).Hash.ToLowerInvariant()
"$hash  $binaryName" | Set-Content -LiteralPath $checksumPath -Encoding ascii

[pscustomobject]@{
    BinaryPath = $binaryPath
    ChecksumPath = $checksumPath
}
