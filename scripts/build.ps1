[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceDir = Join-Path $repoRoot 'src'
$distDir = Join-Path $repoRoot 'dist'
$sourcePath = Join-Path $sourceDir 'BatteryChargeMeter.cs'
$manifestPath = Join-Path $sourceDir 'BatteryChargeMeter.manifest'
$configPath = Join-Path $sourceDir 'BatteryChargeMeter.exe.config'
$outputPath = Join-Path $distDir 'BatteryChargeMeter.exe'

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compilerPath = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $compilerPath) {
    throw 'The .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    "/win32manifest:$manifestPath",
    "/out:$outputPath",
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Management.dll',
    $sourcePath
)

& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $configPath -Destination (Join-Path $distDir 'BatteryChargeMeter.exe.config') -Force

$artifact = Get-Item -LiteralPath $outputPath
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath

Write-Host "Built: $($artifact.FullName)"
Write-Host "Bytes: $($artifact.Length)"
Write-Host "SHA256: $($hash.Hash)"
