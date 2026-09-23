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
$iconPath = Join-Path $sourceDir 'BatteryChargeMeter.ico'
$outputPath = Join-Path $distDir 'PowerMeter.exe'
# Embedded so the portable application remains self-contained. A same-named
# file beside the EXE takes precedence at runtime; see third_party/NOTICE.md.
$modulePath = Join-Path $repoRoot 'third_party/IntelMSR.bin'
$noticePath = Join-Path $repoRoot 'third_party/NOTICE.md'
$licensePath = Join-Path $repoRoot 'third_party/LICENSE.LGPL-2.1.txt'
$expectedModuleHash = 'd6ed85d65ab17a22f813ef98207d6d537155ee2ded5976a21cb48413c9b92e5f'

foreach ($resourcePath in @($modulePath, $noticePath, $licensePath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $resourcePath)) {
        throw "Missing build input: $resourcePath"
    }
}

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $modulePath).Hash.ToLowerInvariant() -ne $expectedModuleHash) {
    throw 'The vendored IntelMSR.bin does not match the pinned PawnIO.Modules 0.2.10 module.'
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
    "/win32icon:$iconPath",
    "/resource:$modulePath,IntelMSR.bin",
    "/resource:$noticePath,THIRD_PARTY_NOTICE.md",
    "/resource:$licensePath,LGPL-2.1.txt",
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
$compatDir = Join-Path $distDir 'compat'
New-Item -ItemType Directory -Path $compatDir | Out-Null
& $compilerPath /nologo /target:winexe /optimize+ "/win32manifest:$manifestPath" "/win32icon:$iconPath" `
    "/out:$compatDir\BatteryChargeMeter.exe" (Join-Path $repoRoot 'installer\LegacyLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Legacy launcher compilation failed.' }
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath

Write-Host "Built: $($artifact.FullName)"
Write-Host "Bytes: $($artifact.Length)"
Write-Host "SHA256: $($hash.Hash)"
