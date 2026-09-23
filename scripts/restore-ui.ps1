[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = '2.4.11'
$expectedHash = '21b856ffa3ec518a0576492fac9c529b7436fead02e6b057af9aa9c0c917b908'
$cache = Join-Path $root ".packages\AntdUI.$version"
$archive = Join-Path $cache 'package.zip'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    $pending = Join-Path $cache 'download.tmp'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/antdui/$version/antdui.$version.nupkg" -OutFile $pending
    if ((Get-FileHash $pending -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        Remove-Item $pending
        throw 'AntdUI package integrity check failed.'
    }
    Move-Item $pending $archive
}
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'Cached AntdUI package integrity check failed.'
}
# Re-extract from the verified archive rather than trusting a mutable cached DLL.
Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force
Join-Path $cache 'lib\net46\AntdUI.dll'
