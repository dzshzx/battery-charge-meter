# SPDX license allowlist guard for the NuGet packages the application ships.
# nuget-license (pinned in .config/dotnet-tools.json) reads project.assets.json, so
# run it after a restore or build (scripts/build.ps1 / scripts/test.ps1).
# Vendored files under third_party/ and installer/Languages/ are not NuGet
# packages; their licenses are recorded in third_party/NOTICE.md.
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$allowed = @(
    'MIT', 'ISC', 'BSD-2-Clause', 'BSD-3-Clause', 'Apache-2.0', '0BSD', 'Unlicense',
    'CC0-1.0', 'Zlib', 'MPL-2.0', 'MS-PL'
) -join ';'
# Reviewed exceptions: the .NET Framework reference assemblies are compile-time
# only (PrivateAssets="all" in Directory.Build.props), never shipped, and declare
# only a license URL.
$ignored = 'Microsoft.NETFramework.ReferenceAssemblies*'

Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" }
    & dotnet tool run nuget-license --input (Join-Path $repoRoot 'src\PowerMeter.csproj') `
        --include-transitive --allowed-license-types $allowed --ignored-packages $ignored
    if ($LASTEXITCODE -ne 0) { throw "license check failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}
