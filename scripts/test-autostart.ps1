[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\dist\PowerMeter.exe'),
    [switch]$GuiOnly,
    [switch]$CheckOrdinaryClient
)

$ErrorActionPreference = 'Stop'
# Framework reflection and COM use the same runtime as the shipping app.
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Executable', $Executable)
    if ($GuiOnly) { $forward += '-GuiOnly' }
    if ($CheckOrdinaryClient) { $forward += '-CheckOrdinaryClient' }
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" @forward
    if ($LASTEXITCODE -ne 0) { throw "Autostart integration failed: $LASTEXITCODE" }
    return
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $GuiOnly -and -not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This isolated test requires an already elevated interactive Windows user. It never requests UAC.'
}
$sid = $identity.User.Value
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$testId = [Guid]::NewGuid().ToString('N')
$taskName = "BatteryChargeMeter.Test.$testId"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "bcm-autostart-$testId"
$copyA = Join-Path $testRoot '版本 A & space\PowerMeter.exe'
$copyB = Join-Path $testRoot 'copy B\Battery Meter.exe'
$copyC = Join-Path $testRoot 'copy C\Battery Meter.exe'
# The shipping code only accepts protected roots at <Program Files>\<app>\<dir>;
# a unique application folder keeps the real startup copy untouched.
$protectedParent = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Power Meter AutostartTest $testId"
$protectedRoot = Join-Path $protectedParent 'autostart'
$protectedDir = Join-Path $protectedRoot $sid
$protectedExe = Join-Path $protectedDir 'PowerMeter.exe'
$service = New-Object -ComObject 'Schedule.Service'
$service.Connect()
$folder = $service.GetFolder('\')
$managerA = $null
$managerB = $null
$managerC = $null
$lease = $null
$task = $null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Host "PASS $Message"
}
function Invoke-Internal($Target, [string]$Name, [object[]]$Arguments = @()) {
    $method = $Target.GetType().GetMethod($Name, [Reflection.BindingFlags]'Instance,Public,NonPublic')
    $plain = New-Object object[] $Arguments.Count
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $plain[$index] = $Arguments[$index].PSObject.BaseObject
    }
    $method.Invoke($Target, $plain)
}
function Enable-Current($Manager) {
    $expected = Invoke-Internal $Manager 'Read'
    Invoke-Internal $Manager 'Enable' @($expected)
}
function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash }
function Copy-Replacing([string]$Source, [string]$Destination) {
    # A freshly written executable can briefly be held open by on-access
    # malware scanning; retry sharing violations instead of failing.
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($true) {
        try { Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop; return }
        catch [IO.IOException] { if ([DateTime]::UtcNow -gt $deadline) { throw } }
        Start-Sleep -Milliseconds 250
    }
}
function Get-TaskCommand { ([xml]$folder.GetTask($taskName).Xml).Task.Actions.Exec.Command }
function Invoke-OrdinaryClient([string]$Operation, [string]$Extra = '') {
    # Use the existing Explorer token, never approximate it by stripping groups.
    $shell = New-Object -ComObject Shell.Application
    $windowHandle = 0
    $desktop = $shell.Windows().FindWindowSW(0, 0, 8, [ref]$windowHandle, 1)
    if (-not $desktop) { throw 'Ordinary-client checks require an interactive Explorer desktop.' }
    $report = Join-Path $testRoot ($Operation + '.json')
    $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $PSScriptRoot 'test-autostart-ordinary-client.ps1') +
        '" -Operation ' + $Operation + ' -Executable "' + $copyA + '" -TaskName ' + $taskName + ' -Report "' + $report + '"' + $Extra
    $desktop.Document.Application.ShellExecute("$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe", $arguments, $testRoot, 'open', 0)
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $report) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path -LiteralPath $report)) { throw "Ordinary $Operation client did not return a report." }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    Assert-True (-not $result.elevated -and $result.sid -eq $sid -and $result.success) "ordinary Explorer token $Operation succeeds: $($result.error)"
    # A report precedes process exit; let the helper release its loaded assembly.
    do {
        $clients = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($report) })
        if ($clients.Count -eq 0) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Ordinary client did not terminate after writing its report.'
}
function Get-TestProcesses {
    @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -in @($copyA, $copyB, $copyC, $protectedExe) })
}
function Wait-TestProcess([int]$Expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $found = @(Get-TestProcesses)
        if ($found.Count -eq $Expected) { return $found }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Expected $Expected isolated processes, found $($found.Count)."
}
function Wait-HiddenWindow([int]$ProcessId) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $result = [BcmAutostartWindowProbe]::Inspect($ProcessId)
        if ($result -eq 1) { return }
        if ($result -eq 2) { throw 'Autostart displayed its main window.' }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Autostart did not create a ready hidden meter window.'
}
function Wait-VisibleWindow([int]$ProcessId) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if ([BcmAutostartWindowProbe]::Inspect($ProcessId) -eq 2) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Manual launch did not restore the existing main window.'
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BcmAutostartWindowProbe {
    private delegate bool Callback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
    public static bool Elevated(int processId) {
        IntPtr process = OpenProcess(0x1000, false, processId);
        IntPtr token = IntPtr.Zero;
        try {
            if (process == IntPtr.Zero || !OpenProcessToken(process, 8, out token)) throw new System.ComponentModel.Win32Exception();
            int elevated, returned;
            if (!GetTokenInformation(token, 20, out elevated, 4, out returned)) throw new System.ComponentModel.Win32Exception();
            return elevated != 0;
        } finally {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }
    public static int Inspect(int process) {
        int result = 0;
        EnumWindows(delegate(IntPtr window, IntPtr ignored) {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != process) return true;
            StringBuilder title = new StringBuilder(256);
            GetWindowText(window, title, 256);
            if (title.ToString() == "Power Meter" || title.ToString() == "功率计") result = IsWindowVisible(window) ? 2 : 1;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

try {
    New-Item -ItemType Directory -Path (Split-Path $copyA), (Split-Path $copyB), (Split-Path $copyC) -Force | Out-Null
    Copy-Item -LiteralPath $Executable -Destination $copyA
    Copy-Item -LiteralPath $Executable -Destination $copyB
    Copy-Item -LiteralPath $Executable -Destination $copyC
    $assembly = [Reflection.Assembly]::LoadFile($Executable)
    $managerType = $assembly.GetType('BatteryChargeMeter.AutostartManager', $true)
    $constructor = $managerType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([string], [string], [string], [string]), $null)
    $managerA = $constructor.Invoke([object[]]@([string]$copyA, [string]$sid, [string]$taskName, [string]$protectedRoot))
    $managerB = $constructor.Invoke([object[]]@([string]$copyB, [string]$sid, [string]$taskName, [string]$protectedRoot))
    $managerC = $constructor.Invoke([object[]]@([string]$copyC, [string]$sid, [string]$taskName, [string]$protectedRoot))
    $absent = Invoke-Internal $managerA 'Read'
    Assert-True (-not (Invoke-Internal $managerA 'Read').Exists) 'unique test task starts absent'
    $leaseType = $assembly.GetType('BatteryChargeMeter.GuiInstance', $true)
    $lease = [Activator]::CreateInstance($leaseType, $true)
    Assert-True (Invoke-Internal $lease 'Acquire' @($copyA, $false)) 'real GUI instance lock can be acquired'
    $lease.Dispose()
    $lease = $null
    if ($GuiOnly) {
        $legacy = Join-Path (Split-Path $copyA) 'BatteryChargeMeter.exe'
        Copy-Item (Join-Path $PSScriptRoot '..\dist\compat\BatteryChargeMeter.exe') $legacy
        $forwarder = Start-Process -FilePath $legacy -ArgumentList '--autostart' -PassThru
        Assert-True ($forwarder.WaitForExit(10000) -and $forwarder.ExitCode -eq 0) 'legacy logon action starts the renamed application'
        $running = @(Wait-TestProcess 1)
        $child = Get-Process -Id $running[0].ProcessId
        Wait-HiddenWindow $child.Id
        Assert-True (-not $child.HasExited) 'ordinary-token autostart is ready and hidden without UAC'
        $parentElevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Assert-True ([BcmAutostartWindowProbe]::Elevated($child.Id) -eq $parentElevated) 'autostart preserves inherited token without implicit UAC'
        $duplicate = Start-Process -FilePath $copyA -ArgumentList '--autostart' -PassThru
        Assert-True ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) 'ordinary duplicate autostart exits cleanly'
        Wait-HiddenWindow $child.Id
        $duplicate = Start-Process -FilePath $copyA -PassThru
        Assert-True ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) 'ordinary manual launch does not duplicate hidden GUI'
        Wait-VisibleWindow $child.Id
        Assert-True ($true) 'manual launch restores the actual existing hidden window without UAC'
        Assert-True (@(Get-TestProcesses).Count -eq 1) 'exactly one isolated GUI remains'
        Assert-True (-not (Invoke-Internal $managerA 'Read').Exists) 'GUI-only test never registers a task'
        Write-Host 'Autostart GUI-only integration passed.'
        return
    }
    # A task left by an older version runs the user-writable file directly.
    $legacyXml = $managerType.GetMethod('BuildXml', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@([string]$copyA, [string]$sid))
    $folder.RegisterTask($taskName, $legacyXml, 6, $sid, $null, 3, $null) | Out-Null
    $legacy = Invoke-Internal $managerA 'Read'
    Assert-True ($legacy.ThisCopy -and -not $legacy.Protected -and -not $legacy.Enabled -and $legacy.RepairReason) 'task running the user-writable file is reported as needing migration'
    Assert-True ([int](Invoke-Internal $managerA 'Synchronize' @($false)) -eq 4) 'report-only synchronization flags the unprotected task'
    Assert-True ([int](Invoke-Internal $managerA 'Synchronize' @($true)) -eq 0 -and (Get-TaskCommand) -eq $protectedExe) 'elevated synchronization migrates the task to the protected copy'
    Invoke-Internal $managerA 'Disable' | Out-Null
    Assert-True (-not (Invoke-Internal $managerA 'Read').Exists -and -not (Test-Path -LiteralPath $protectedExe)) 'elevated disable removes task and protected copy'

    Enable-Current $managerA
    $state = Invoke-Internal $managerA 'Read'
    Assert-True ($state.Enabled -and $state.ThisCopy -and $state.Protected -and $state.Executable -eq $copyA) 'register and read back exact elevated logon task'
    Assert-True ((Get-TaskCommand) -eq $protectedExe -and (Get-Sha256 $protectedExe) -eq (Get-Sha256 $copyA)) 'task runs a byte-identical copy in the admin-only directory'
    Enable-Current $managerA
    Assert-True ((Invoke-Internal $managerA 'Read').Enabled) 'idempotent update reads back enabled'
    $task = $folder.GetTask($taskName)
    [xml]$xml = $task.Xml
    Assert-True ($xml.Task.Principals.Principal.RunLevel -eq 'HighestAvailable' -and $xml.Task.Principals.Principal.LogonType -eq 'InteractiveToken' -and $xml.Task.Principals.Principal.UserId -eq $sid) 'explicit user SID, highest token, no stored password'
    $settings = $task.Definition.Settings
    Assert-True (-not $settings.DisallowStartIfOnBatteries -and -not $settings.StopIfGoingOnBatteries -and -not $settings.RunOnlyIfIdle -and -not $settings.RunOnlyIfNetworkAvailable -and $settings.ExecutionTimeLimit -eq 'PT0S') 'battery, idle, network and execution-limit settings'
    Assert-True ($xml.Task.Settings.MultipleInstancesPolicy -eq 'IgnoreNew') 'scheduler prevents duplicate scheduled instances'
    $replacementRejected = $false
    try { Invoke-Internal $managerB 'Enable' @($absent) } catch { $replacementRejected = $true }
    Assert-True ($replacementRejected -and (Invoke-Internal $managerA 'Read').ThisCopy) 'another copy cannot silently repoint the task'
    $confirmedA = Invoke-Internal $managerB 'Read'
    Enable-Current $managerC
    $staleRejected = $false
    try { Invoke-Internal $managerB 'Enable' @($confirmedA) } catch { $staleRejected = $true }
    Assert-True ($staleRejected -and (Invoke-Internal $managerC 'Read').ThisCopy) 'stale A-to-B confirmation cannot overwrite concurrent copy C'
    Enable-Current $managerB
    Invoke-Internal $managerA 'Disable' | Out-Null
    Assert-True ((Invoke-Internal $managerB 'Read').ThisCopy -and (Test-Path -LiteralPath $protectedExe)) 'old installation cleanup preserves another copy task and its protected copy'
    Invoke-Internal $managerB 'Disable' | Out-Null
    Assert-True (-not (Invoke-Internal $managerB 'Read').Exists -and -not (Test-Path -LiteralPath $protectedExe)) 'own task and protected copy deletion reads back absent'

    Enable-Current $managerA
    $task = $folder.GetTask($taskName)
    $changedXml = $task.Xml.Replace('<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>', '<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>')
    $folder.RegisterTask($taskName, $changedXml, 6, $sid, $null, 3, $null) | Out-Null
    $drifted = Invoke-Internal $managerA 'Read'
    Assert-True (-not $drifted.Enabled -and $drifted.RepairReason) 'real task policy drift is reported as needing repair'
    Enable-Current $managerA
    Assert-True ((Invoke-Internal $managerA 'Read').Enabled) 're-enable repairs drift and reads back enabled'
    $task = $folder.GetTask($taskName)

    # Replace the user-writable executable, as any ordinary process can. The
    # elevated scheduler launch must still run the unchanged protected copy.
    $originalHash = Get-Sha256 $copyA
    $backupA = Join-Path $testRoot 'copy-a-backup.exe'
    Copy-Item -LiteralPath $copyA -Destination $backupA
    $replacement = "$env:WINDIR\System32\whoami.exe"
    if ($CheckOrdinaryClient) {
        Invoke-OrdinaryClient 'Replace' (' -Replacement "' + $replacement + '" -ProtectedExecutable "' + $protectedExe + '"')
    } else {
        Copy-Replacing $replacement $copyA
    }
    Assert-True ((Get-Sha256 $copyA) -eq (Get-Sha256 $replacement)) 'user-writable executable was replaced'
    $task.Run($null) | Out-Null
    $running = @(Wait-TestProcess 1)
    Wait-HiddenWindow $running[0].ProcessId
    Assert-True ($running[0].ExecutablePath -eq $protectedExe -and (Get-Sha256 $protectedExe) -eq $originalHash) 'scheduler still runs the original protected copy after replacement'
    Assert-True ([BcmAutostartWindowProbe]::Elevated($running[0].ProcessId)) 'scheduler actually launched an elevated token'
    $task.Stop(0)
    Wait-TestProcess 0 | Out-Null
    Copy-Replacing $backupA $copyA
    $task.Run($null) | Out-Null
    $running = @(Wait-TestProcess 1)
    Wait-HiddenWindow $running[0].ProcessId
    Assert-True ($running[0].CommandLine -match '--autostart') 'actual scheduler action starts hidden tray GUI'
    $task.Run($null) | Out-Null
    Start-Sleep -Milliseconds 500
    Assert-True (@(Get-TestProcesses).Count -eq 1) 'second scheduler Run does not duplicate GUI'
    $duplicate = Start-Process -FilePath $copyA -ArgumentList '--autostart' -PassThru
    Assert-True ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) 'duplicate autostart exits cleanly'
    Wait-HiddenWindow $running[0].ProcessId
    if ($CheckOrdinaryClient) {
        Invoke-OrdinaryClient 'Restore'
        Wait-VisibleWindow $running[0].ProcessId
        Assert-True (@(Get-TestProcesses).Count -eq 1) 'ordinary launch restores elevated window without duplication'
    }
    $duplicate = Start-Process -FilePath $copyA -ArgumentList '--no-elevate' -PassThru
    Assert-True ($duplicate.WaitForExit(10000) -and $duplicate.ExitCode -eq 0) 'manual launch preserves existing scheduled instance'
    Wait-VisibleWindow $running[0].ProcessId
    Assert-True ($true) 'manual launch restores actual scheduled window'
    $task.Stop(0)
    Wait-TestProcess 0 | Out-Null

    # Simulate the old GUI holding the same real mutex during a runas handoff.
    $leaseType = $assembly.GetType('BatteryChargeMeter.GuiInstance', $true)
    $lease = [Activator]::CreateInstance($leaseType, $true)
    Assert-True (Invoke-Internal $lease 'Acquire' @($copyA, $false)) 'parent owns real handoff lock'
    $child = Start-Process -FilePath $copyA -ArgumentList '--elevation-attempted' -PassThru
    Start-Sleep -Milliseconds 500
    Assert-True (-not $child.HasExited) 'elevated handoff child waits for old GUI'
    $lease.Dispose()
    $lease = $null
    Start-Sleep -Seconds 2
    $child.Refresh()
    Assert-True (-not $child.HasExited) 'handoff child survives parent release'
    $task.Run($null) | Out-Null
    Start-Sleep -Seconds 2
    Assert-True (@(Get-TestProcesses).Count -eq 1) 'scheduled start does not duplicate a manually launched GUI'
    Stop-Process -Id $child.Id -Force
    Wait-TestProcess 0 | Out-Null
    if ($CheckOrdinaryClient) {
        Invoke-OrdinaryClient 'Disable'
        Assert-True (Test-Path -LiteralPath $protectedExe) 'ordinary-token disable leaves the admin-only copy'
        Assert-True ([int](Invoke-Internal $managerA 'Synchronize' @($false)) -eq 5) 'report-only synchronization flags the unused copy'
        Invoke-Internal $managerA 'Synchronize' @($true) | Out-Null
    } else {
        Invoke-Internal $managerA 'Disable' | Out-Null
    }
    Assert-True (-not (Invoke-Internal $managerA 'Read').Exists -and -not (Test-Path -LiteralPath $protectedParent)) 'final removal reads back absent task, copy and empty folders'
    Write-Host 'Autostart integration passed. Only the unique test task and temporary copies were touched.'
}
finally {
    if ($lease) { $lease.Dispose() }
    # Exact generated name and exact temporary executable paths only.
    foreach ($process in @(Get-TestProcesses)) {
        Stop-Process -Id $process.ProcessId -Force -PassThru -ErrorAction SilentlyContinue |
            Wait-Process -Timeout 10
    }
    try { $folder.DeleteTask($taskName, 0) } catch {
        $cleanupError = $_.Exception
        while ($cleanupError.InnerException) { $cleanupError = $cleanupError.InnerException }
        $errorCode = $cleanupError.HResult
        if ($errorCode -ne -2147024894) { Write-Warning "Check isolated test task cleanup: $taskName : $_" }
    }
    if ($managerA) { $managerA.Dispose() }
    if ($managerB) { $managerB.Dispose() }
    if ($managerC) { $managerC.Dispose() }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
    if (Test-Path -LiteralPath $protectedParent) { Remove-Item -LiteralPath $protectedParent -Recurse -Force }
}
