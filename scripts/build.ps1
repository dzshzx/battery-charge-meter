[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceDir = Join-Path $repoRoot 'src'
$distDir = Join-Path $repoRoot 'dist'
$sourcePaths = @(
    Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File |
        Sort-Object -Property Name |
        Select-Object -ExpandProperty FullName
)
$manifestPath = Join-Path $sourceDir 'BatteryChargeMeter.manifest'
$outputPath = Join-Path $distDir 'BatteryChargeMeter.exe'
# Embedded so the build stays a single file. A same-named file beside the EXE
# takes precedence at runtime; see third_party/NOTICE.md.
$modulePath = Join-Path $repoRoot 'third_party/IntelMSR.bin'

if (-not (Test-Path -LiteralPath $modulePath)) {
    throw "Missing PawnIO module: $modulePath"
}

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

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    "/win32manifest:$manifestPath",
    "/resource:$modulePath,IntelMSR.bin",
    "/out:$outputPath",
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Management.dll',
    $sourcePaths
)

& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$artifact = Get-Item -LiteralPath $outputPath
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath

Write-Host "Built: $($artifact.FullName)"
Write-Host "Bytes: $($artifact.Length)"
Write-Host "SHA256: $($hash.Hash)"
