[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distDir = Join-Path $repoRoot 'dist'
$outputPath = Join-Path $distDir 'PowerMeter.exe'
# Resources embedded by src/PowerMeter.csproj; checked here for a clear error.
$modulePath = Join-Path $repoRoot 'third_party/IntelMSR.bin'
$expectedModuleHash = 'd6ed85d65ab17a22f813ef98207d6d537155ee2ded5976a21cb48413c9b92e5f'
$inputs = @(
    $modulePath,
    (Join-Path $repoRoot 'third_party/NOTICE.md'),
    (Join-Path $repoRoot 'third_party/LICENSE.LGPL-2.1.txt'),
    (Join-Path $repoRoot 'src/BatteryChargeMeter.ico')
)

foreach ($resourcePath in $inputs) {
    if (-not (Test-Path -LiteralPath $resourcePath)) {
        throw "Missing build input: $resourcePath"
    }
}

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $modulePath).Hash.ToLowerInvariant() -ne $expectedModuleHash) {
    throw 'The vendored IntelMSR.bin does not match the pinned PawnIO.Modules 0.2.10 module.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found.'
}

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# RestoreLockedMode fails the restore if packages.lock.json is stale; NuGet checks
# each package against the content hash recorded there.
function Invoke-ProjectBuild([string]$project, [string]$output) {
    & dotnet build (Join-Path $repoRoot $project) --configuration Release -p:RestoreLockedMode=true --nologo --output $output
    if ($LASTEXITCODE -ne 0) {
        throw "Build of $project failed with exit code $LASTEXITCODE."
    }
}

Invoke-ProjectBuild 'src/PowerMeter.csproj' $distDir
$artifact = Get-Item -LiteralPath $outputPath
Invoke-ProjectBuild 'installer/LegacyLauncher.csproj' (Join-Path $distDir 'compat')
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath

Write-Host "Built: $($artifact.FullName)"
Write-Host "Bytes: $($artifact.Length)"
Write-Host "SHA256: $($hash.Hash)"
