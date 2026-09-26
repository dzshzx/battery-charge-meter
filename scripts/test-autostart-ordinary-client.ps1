[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Restore', 'Disable', 'Replace')][string]$Operation,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][ValidatePattern('^BatteryChargeMeter\.Test\.[0-9a-f]{32}$')][string]$TaskName,
    [Parameter(Mandatory)][string]$Report,
    [string]$Replacement,
    [string]$ProtectedExecutable
)

# Run this helper through the interactive Explorer user's ordinary token.
# The elevated integration owner creates the isolated task/copy first and
# checks window visibility or task absence after this helper exits.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Operation $Operation -Executable $Executable -TaskName $TaskName -Report $Report -Replacement $Replacement -ProtectedExecutable $ProtectedExecutable
    exit $LASTEXITCODE
}
$id = $TaskName.Substring('BatteryChargeMeter.Test.'.Length)
$root = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "bcm-autostart-$id")) + '\'
$Executable = [IO.Path]::GetFullPath($Executable)
$Report = [IO.Path]::GetFullPath($Report)
if (-not $Executable.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    -not $Report.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only the matching isolated temporary test directory is allowed.'
}
$protectedTestRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Power Meter AutostartTest $id")) + '\'
if ($Operation -eq 'Replace' -and (-not $ProtectedExecutable -or -not $Replacement -or
    -not [IO.Path]::GetFullPath($ProtectedExecutable).StartsWith($protectedTestRoot, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Replace needs a replacement file and the matching isolated protected copy.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$result = [ordered]@{ operation = $Operation; elevated = $elevated; sid = $identity.User.Value; success = $false; error = $null }
$manager = $null
try {
    if ($elevated) { throw 'This client must run with the ordinary interactive token.' }
    if ($Operation -eq 'Restore') {
        $process = Start-Process -FilePath $Executable -PassThru
        if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) {
            throw 'Manual restore client did not exit cleanly without UAC.'
        }
    } elseif ($Operation -eq 'Replace') {
        # The user-writable copy must be replaceable by this ordinary token;
        # the protected copy and its directory must not be.
        Copy-Item -LiteralPath $Replacement -Destination $Executable -Force
        $protectedDirectory = Split-Path -Parent $ProtectedExecutable
        $attempts = @(
            { [IO.File]::Open($ProtectedExecutable, 'Open', 'Write', 'ReadWrite').Dispose() },
            { [IO.File]::WriteAllText((Join-Path $protectedDirectory 'planted.txt'), 'planted') },
            { [IO.File]::Move($ProtectedExecutable, $ProtectedExecutable + '.moved') },
            { [IO.File]::Delete($ProtectedExecutable) },
            { $acl = Get-Acl -LiteralPath $ProtectedExecutable; $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User, 'FullControl', 'Allow'))); Set-Acl -LiteralPath $ProtectedExecutable -AclObject $acl }
        )
        foreach ($attempt in $attempts) {
            $allowed = $true
            try { & $attempt } catch { $allowed = $false }
            if ($allowed) { throw "Ordinary token modified the protected copy: $attempt" }
        }
        if (-not (Test-Path -LiteralPath $ProtectedExecutable)) { throw 'Protected copy disappeared.' }
    } else {
        $assembly = [Reflection.Assembly]::LoadFile($Executable)
        $type = $assembly.GetType('BatteryChargeMeter.AutostartManager', $true)
        $flags = [Reflection.BindingFlags]'Instance,NonPublic'
        $constructor = $type.GetConstructor($flags, $null, [type[]]@([string], [string], [string], [string]), $null)
        $protectedRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Power Meter AutostartTest $id\autostart"
        $manager = $constructor.Invoke([object[]]@([string]$Executable, [string]$identity.User.Value, [string]$TaskName, [string]$protectedRoot))
        $before = $type.GetMethod('Read', $flags).Invoke($manager, $null)
        if (-not $before.Exists -or -not $before.ThisCopy) { throw 'Expected the isolated elevated-created task targeting this copy.' }
        $type.GetMethod('Disable', $flags).Invoke($manager, $null)
        $after = $type.GetMethod('Read', $flags).Invoke($manager, $null)
        if ($after.Exists) { throw 'Task still exists after ordinary-token deletion.' }
    }
    $result.success = $true
} catch {
    $errorDetail = $_.Exception
    while ($errorDetail.InnerException) { $errorDetail = $errorDetail.InnerException }
    $result.error = $errorDetail.Message
} finally {
    if ($manager) { $manager.Dispose() }
    $result | ConvertTo-Json | Set-Content -LiteralPath $Report -Encoding UTF8
}
if (-not $result.success) { exit 1 }
