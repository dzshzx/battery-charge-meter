[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerCompilerPath,
    [Parameter(Mandatory)][string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -InstallerCompilerPath $InstallerCompilerPath -ExecutablePath $ExecutablePath
    if ($LASTEXITCODE -ne 0) { throw "Installer lifecycle test failed: $LASTEXITCODE" }
    return
}
$repo = Split-Path $PSScriptRoot -Parent
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # Setup refreshes and uninstall removes the protected logon copy below
    # Program Files. Without elevation both would show UAC, which this
    # unattended test must never do.
    throw 'The installer lifecycle test requires an already elevated token. It never requests UAC.'
}
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path ([IO.Path]::GetTempPath()) "bcm-installer-$id"
$installDir = Join-Path $root 'installed'
$installedExe = Join-Path $installDir 'PowerMeter.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$receipt = Join-Path $root 'cleanup-called.txt'
$syncReceipt = Join-Path $root 'sync-called.txt'
$failureFlag = Join-Path $root 'force-cleanup-failure'
$taskName = "BatteryChargeMeter.InstallerTest.$id"
$appId = "PowerMeterTest.$id"
$shortcutName = "Battery Charge Meter Installer Test $id"
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$shortcutName.lnk"
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
$sid = $identity.User.Value
# A unique application folder below Program Files stands in for
# "Power Meter"; the shipping code only accepts roots at that depth.
$protectedParent = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Power Meter InstallerTest $id"
$protectedRoot = Join-Path $protectedParent 'autostart'
$protectedDir = Join-Path $protectedRoot $sid
$protectedExe = Join-Path $protectedDir 'PowerMeter.exe'
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$installed = $false
$activeUninstaller = $null
$uninstallProcessIds = @()

function Assert-Installer([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Host "PASS $Message"
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
function Get-TaskXml {
    try { return $folder.GetTask($taskName).Xml } catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        if ($cause.HResult -ne -2147024894) { throw }
        return $null
    }
}
function Get-TaskCommand {
    $current = Get-TaskXml
    if (-not $current) { return $null }
    return ([xml]$current).Task.Actions.Exec.Command
}
function Test-AdminOnlyWrite([string]$Path) {
    # Any write, delete, ownership or permission right held by a principal
    # other than SYSTEM/Administrators would let an ordinary token swap files.
    $security = Get-Acl -LiteralPath $Path
    $owner = $security.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($owner -ne 'S-1-5-32-544' -and $owner -ne 'S-1-5-18') { return $false }
    $writeMask = 0x2 -bor 0x4 -bor 0x10 -bor 0x40 -bor 0x100 -bor 0x10000 -bor 0x40000 -bor 0x80000 -bor 0x10000000 -bor 0x40000000
    foreach ($rule in $security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        if ($rule.IdentityReference.Value -in @('S-1-5-18', 'S-1-5-32-544')) { continue }
        if (([int]$rule.FileSystemRights -band $writeMask) -ne 0) { return $false }
    }
    return $true
}
function Start-Uninstall([int]$Answer, [string]$LogName) {
    $script:activeUninstaller = Start-Process -FilePath $uninstaller -ArgumentList @('/NORESTART', ('/LOG="{0}"' -f (Join-Path $root $LogName))) -PassThru
    $owned = New-Object 'System.Collections.Generic.HashSet[int]'
    $owned.Add($script:activeUninstaller.Id) | Out-Null
    $answered = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $stillRunning = $true
    while ($stillRunning -and [DateTime]::UtcNow -lt $deadline) {
        # Inno's first phase runs a temporary second-phase uninstaller. Only
        # dialogs belonging to this exact process tree may receive clicks.
        $processes = @(Get-CimInstance Win32_Process)
        foreach ($process in $processes) {
            if ($owned.Contains([int]$process.ParentProcessId)) { $owned.Add([int]$process.ProcessId) | Out-Null }
        }
        $ids = [int[]]@($owned)
        $script:uninstallProcessIds = @($processes | Where-Object { $owned.Contains([int]$_.ProcessId) } | Select-Object -ExpandProperty ProcessId)
        $stillRunning = $script:uninstallProcessIds.Count -gt 0
        if (-not $stillRunning) { break }
        if (-not $answered) {
            $answered = [BcmUninstallDialog]::Click($ids, $Answer)
        } elseif ($Answer -eq 6) {
            # Dismiss only this uninstaller's success/error acknowledgement.
            [BcmUninstallDialog]::Click($ids, 1) | Out-Null
        }
        Start-Sleep -Milliseconds 100
        $script:activeUninstaller.Refresh()
    }
    if ($stillRunning) { throw ('Uninstaller dialog test timed out. ' + [BcmUninstallDialog]::Describe([int[]]@($owned))) }
    Assert-Installer $answered 'real Inno confirmation dialog received the requested answer'
    $script:activeUninstaller.WaitForExit()
    return $script:activeUninstaller.ExitCode
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BcmUninstallDialog {
    private delegate bool Callback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Callback callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static string Describe(int[] owners) {
        StringBuilder result = new StringBuilder();
        EnumWindows(delegate(IntPtr window, IntPtr ignored) {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (Array.IndexOf(owners, (int)owner) < 0 || !IsWindowVisible(window)) return true;
            EnumChildWindows(window, delegate(IntPtr child, IntPtr unused) {
                StringBuilder text = new StringBuilder(256);
                GetWindowText(child, text, text.Capacity);
                if (IsWindowVisible(child)) result.AppendFormat("[{0}: {1}]", GetDlgCtrlID(child), text);
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return result.ToString();
    }
    public static bool Click(int[] owners, int id) {
        bool clicked = false;
        EnumWindows(delegate(IntPtr window, IntPtr ignored) {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (Array.IndexOf(owners, (int)owner) < 0 || !IsWindowVisible(window)) return true;
            IntPtr button = GetDlgItem(window, id);
            if ((button == IntPtr.Zero || !IsWindowVisible(button)) && id == 1) {
                // Windows may assign IDCANCEL to the sole acknowledgement
                // button of an exception message box. Restrict this fallback
                // to native dialogs, never the progress form's Cancel button.
                StringBuilder windowClass = new StringBuilder(64);
                GetClassName(window, windowClass, windowClass.Capacity);
                if (windowClass.ToString() == "#32770" && GetDlgItem(window, 6) == IntPtr.Zero && GetDlgItem(window, 7) == IntPtr.Zero)
                    button = GetDlgItem(window, 2);
            }
            if (button == IntPtr.Zero || !IsWindowVisible(button)) return true;
            clicked = PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
            return !clicked;
        }, IntPtr.Zero);
        return clicked;
    }
}
'@

try {
    New-Item -ItemType Directory -Path $root | Out-Null
    # Only identities change in this fixture; the shipped uninstall hook is
    # compiled verbatim. Never install with the real AppId or Start Menu name.
    $iss = Get-Content (Join-Path $repo 'installer\BatteryChargeMeter.iss') -Raw -Encoding UTF8
    $iss = $iss.Replace('AppId={{FDDC9FC9-109E-4B41-AE4A-BA30420295D0}', "AppId=$appId")
    $iss = $iss.Replace('Name: "{autoprograms}\{cm:ApplicationName}";', ('Name: "{{autoprograms}}\{0}";' -f $shortcutName))
    Assert-Installer ($iss.Contains("AppId=$appId") -and $iss.Contains($shortcutName)) 'fixture has isolated installer identities'
    $issPath = Join-Path $root 'fixture.iss'
    $iss = '#define ChineseMessages "' + $repo + '\installer\Languages\ChineseSimplified.isl"' + [Environment]::NewLine + $iss
    Set-Content -LiteralPath $issPath -Value $iss -Encoding UTF8

    # The fixture entry point routes cleanup and startup synchronization to
    # the shipping manager with a unique task name and protected-copy root. It
    # also injects a nonzero cleanup result for the fatal failure case; no
    # production test flag or real autostart task is needed. Two builds give
    # the upgrade a different executable to copy.
    $code = @'
using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Diagnostics;
class InstallerCleanupFixture {
    const string Build = "__BUILD__";
    static int Main(string[] args) {
        if (args.Length != 1 || (args[0] != "--remove-autostart" && args[0] != "--sync-autostart")) return 2;
        bool remove = args[0] == "--remove-autostart";
        File.AppendAllText(remove ? @"__RECEIPT__" : @"__SYNC__", Build + "\r\n");
        if (remove && File.Exists(@"__FAILURE__")) return 1;
        object manager = null;
        try {
            Assembly app = Assembly.LoadFile(@"__PAYLOAD__");
            Type type = app.GetType("BatteryChargeMeter.AutostartManager", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            WindowsIdentity user = WindowsIdentity.GetCurrent();
            bool elevated = new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator);
            manager = type.GetConstructor(flags, null, new Type[] {typeof(string), typeof(string), typeof(string), typeof(string)}, null)
                .Invoke(new object[] {Process.GetCurrentProcess().MainModule.FileName, user.User.Value, "__TASK__", @"__ROOT__"});
            if (remove) return Convert.ToInt32(type.GetMethod("Disable", flags).Invoke(manager, null));
            return Convert.ToInt32(type.GetMethod("Synchronize", flags).Invoke(manager, new object[] {elevated}));
        } catch { return 1; }
        finally { if (manager != null) ((IDisposable)manager).Dispose(); }
    }
}
'@
    $code = $code.Replace('__RECEIPT__', $receipt.Replace('"', '""')).Replace('__SYNC__', $syncReceipt.Replace('"', '""')).Replace('__FAILURE__', $failureFlag.Replace('"', '""')).Replace('__PAYLOAD__', $ExecutablePath.Replace('"', '""')).Replace('__TASK__', $taskName).Replace('__ROOT__', $protectedRoot.Replace('"', '""'))
    $fixtureExe = Join-Path $root 'fixture.exe'
    $upgradeExe = Join-Path $root 'fixture-upgrade.exe'
    foreach ($build in @(@{ Name = '1'; Output = $fixtureExe }, @{ Name = '2'; Output = $upgradeExe })) {
        $fixtureSource = Join-Path $root ("fixture{0}.cs" -f $build.Name)
        Set-Content -LiteralPath $fixtureSource -Value $code.Replace('__BUILD__', $build.Name) -Encoding UTF8
        & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$($build.Output)" $fixtureSource
        if ($LASTEXITCODE -ne 0) { throw 'Cleanup fixture compilation failed.' }
    }
    Assert-Installer ((Get-Sha256 $fixtureExe) -ne (Get-Sha256 $upgradeExe)) 'upgrade fixture differs from the installed executable'
    foreach ($setupBuild in @(@{ Version = '9.8.7'; Exe = $fixtureExe; Name = 'fixture-setup' }, @{ Version = '9.8.8'; Exe = $upgradeExe; Name = 'fixture-upgrade-setup' })) {
        & $InstallerCompilerPath '/Q' "/DAppVersion=$($setupBuild.Version)" "/DSourceExe=$($setupBuild.Exe)" "/DLegacyLauncher=$repo\dist\compat\BatteryChargeMeter.exe" "/DNoticePath=$repo\third_party\NOTICE.md" "/DLicensePath=$repo\third_party\LICENSE.LGPL-2.1.txt" "/DOutputDir=$root" "/DOutputBaseFilename=$($setupBuild.Name)" $issPath
        if ($LASTEXITCODE -ne 0) { throw 'Real installer compilation failed.' }
    }
    $setup = Start-Process (Join-Path $root 'fixture-setup.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=en', ('/LOG="{0}"' -f (Join-Path $root 'install.log')), ('/DIR="{0}"' -f $installDir)) -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Isolated install failed: $($setup.ExitCode)" }
    $installed = $true
    foreach ($name in @('PowerMeter.exe', 'THIRD-PARTY-NOTICES.txt', 'LICENSE.LGPL-2.1.txt', 'unins000.exe', 'unins000.dat')) {
        Assert-Installer (Test-Path -LiteralPath (Join-Path $installDir $name)) "installer provides $name"
    }
    $legacyExe = Join-Path $installDir 'BatteryChargeMeter.exe'
    Assert-Installer (-not (Test-Path -LiteralPath $legacyExe)) 'fresh install uses only the new executable name'
    $installedName = (Get-ItemProperty $registryPath).DisplayName
    Assert-Installer ($installedName -eq 'Power Meter') "English installer uses Power Meter branding: $installedName"
    Assert-Installer ((Test-Path -LiteralPath $syncReceipt) -and -not (Get-TaskXml) -and -not (Test-Path -LiteralPath $protectedParent)) 'install checks startup but creates no task or protected copy while startup is off'

    # Simulate an upgrade in the same AppId and directory over an older
    # version whose logon task still runs the user-writable old executable.
    # Setup must migrate it to the admin-only protected copy.
    Copy-Item -LiteralPath $fixtureExe -Destination $legacyExe
    $app = [Reflection.Assembly]::LoadFile($ExecutablePath)
    $type = $app.GetType('BatteryChargeMeter.AutostartManager', $true)
    $xml = $type.GetMethod('BuildXml', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@([string]$legacyExe, [string]$sid))
    # Registered disabled and least-privilege so it can never execute the
    # user-writable fixture. Its ownership marker, exact executable, SID and
    # action arguments still identify this installation's legacy task.
    $xml = $xml.Replace('HighestAvailable', 'LeastPrivilege').Replace('<Enabled>true</Enabled>', '<Enabled>false</Enabled>')
    $folder.RegisterTask($taskName, $xml, 2, $sid, $null, 3, $null) | Out-Null
    $upgrade = Start-Process (Join-Path $root 'fixture-setup.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=zhCN', ('/DIR="{0}"' -f $installDir)) -Wait -PassThru
    Assert-Installer ($upgrade.ExitCode -eq 0) 'Chinese upgrade completes in the existing installation'
    Assert-Installer ((Get-ItemProperty $registryPath).DisplayName -eq '功率计') 'Chinese installer uses localized branding'
    Assert-Installer ([Diagnostics.FileVersionInfo]::GetVersionInfo($legacyExe).FileDescription -eq 'Power Meter legacy launcher') 'upgrade replaces the old executable with the compatibility launcher'
    [xml]$migrated = Get-TaskXml
    Assert-Installer ($migrated.Task.Actions.Exec.Command -eq $protectedExe -and $migrated.Task.Actions.Exec.WorkingDirectory -eq $protectedDir) 'upgrade repoints the legacy logon task from the user-writable launcher to the protected copy'
    Assert-Installer ($migrated.Task.Principals.Principal.RunLevel -eq 'HighestAvailable' -and $migrated.Task.Principals.Principal.UserId -eq $sid) 'migrated task keeps the elevated interactive user policy'
    Assert-Installer ((Get-Sha256 $protectedExe) -eq (Get-Sha256 $installedExe)) 'protected copy is byte-identical to the installed executable'
    Assert-Installer ((Get-Content -LiteralPath (Join-Path $protectedDir 'source.txt') -Raw -Encoding UTF8).Trim() -eq $installedExe) 'protected copy records its owning installation'
    foreach ($protectedPath in @($protectedParent, $protectedRoot, $protectedDir, $protectedExe)) {
        Assert-Installer (Test-AdminOnlyWrite $protectedPath) "only SYSTEM and Administrators can modify $protectedPath"
    }
    Assert-Installer ((Get-Acl -LiteralPath $protectedRoot).AreAccessRulesProtected) 'protected copy root does not inherit parent permissions'
    $aliasMethod = $type.GetMethod('IsCurrentCopy', [Reflection.BindingFlags]'Static,NonPublic')
    Assert-Installer ($aliasMethod.Invoke($null, [object[]]@([string]$legacyExe, [string]$installedExe))) 'new application recognizes its installed legacy launcher'
    Assert-Installer (-not $aliasMethod.Invoke($null, [object[]]@([string]$legacyExe, [string]$ExecutablePath))) 'another portable copy cannot claim the legacy task'

    # Replacing the user-writable executable must not change what the task
    # runs. (Any ordinary process could perform this write.)
    $protectedHash = Get-Sha256 $protectedExe
    $installedBackup = Join-Path $root 'installed-backup.exe'
    Copy-Item -LiteralPath $installedExe -Destination $installedBackup
    Copy-Replacing "$env:WINDIR\System32\whoami.exe" $installedExe
    Assert-Installer ((Get-TaskCommand) -eq $protectedExe -and (Get-Sha256 $protectedExe) -eq $protectedHash) 'replacing the installed executable leaves the task target and protected copy unchanged'
    Copy-Replacing $installedBackup $installedExe

    $migratedXml = Get-TaskXml
    Remove-Item -LiteralPath $syncReceipt
    $refresh = Start-Process (Join-Path $root 'fixture-upgrade-setup.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=en', ('/DIR="{0}"' -f $installDir)) -Wait -PassThru
    Assert-Installer ($refresh.ExitCode -eq 0 -and (Get-Content -LiteralPath $syncReceipt -Raw).Trim() -eq '2') 'new version upgrade runs its own startup synchronization'
    Assert-Installer ((Get-Sha256 $installedExe) -eq (Get-Sha256 $upgradeExe) -and (Get-Sha256 $protectedExe) -eq (Get-Sha256 $upgradeExe)) 'upgrade refreshes the protected copy to the new executable'
    Assert-Installer ((Get-TaskXml) -eq $migratedXml) 'refreshing the protected copy leaves the task definition unchanged'
    $before = Get-TaskXml
    Set-Content -LiteralPath $failureFlag -Value 'injected nonzero cleanup result'
    $legacyCleanup = Start-Process $legacyExe -ArgumentList '--remove-autostart' -Wait -PassThru
    Assert-Installer ($legacyCleanup.ExitCode -eq 1 -and (Test-Path -LiteralPath $receipt)) 'legacy launcher forwards cleanup and preserves its failure code'
    Remove-Item -LiteralPath $failureFlag, $receipt
    $cancelCode = Start-Uninstall 7 'cancel.log'
    Assert-Installer (-not (Test-Path -LiteralPath $receipt)) 'cancel never invokes startup cleanup'
    Assert-Installer ($folder.GetTask($taskName).Xml -eq $before) 'cancel preserves the actual task unchanged'
    Assert-Installer ((Get-Sha256 $protectedExe) -eq (Get-Sha256 $upgradeExe)) 'cancel preserves the protected copy'
    Assert-Installer ((Test-Path -LiteralPath $installedExe) -and (Test-Path -LiteralPath $uninstaller)) 'cancel preserves installed executable and uninstaller'

    Set-Content -LiteralPath $failureFlag -Value 'injected nonzero cleanup result'
    $failureCode = Start-Uninstall 6 'failure.log'
    Assert-Installer ($failureCode -ne 0 -and (Test-Path -LiteralPath $receipt)) 'affirmative uninstall propagates cleanup failure'
    Assert-Installer ($folder.GetTask($taskName).Xml -eq $before) 'cleanup failure preserves the actual task'
    Assert-Installer (Test-Path -LiteralPath $protectedExe) 'cleanup failure preserves the protected copy'
    Assert-Installer ((Test-Path -LiteralPath $installedExe) -and (Test-Path -LiteralPath $uninstaller) -and (Test-Path -LiteralPath (Join-Path $installDir 'unins000.dat'))) 'fatal hook exception preserves application and uninstall data'
    Remove-Item -LiteralPath $failureFlag
    $acceptedCode = Start-Uninstall 6 'accept.log'
    Assert-Installer ($acceptedCode -eq 0) 'affirmative uninstall succeeds after cleanup succeeds'
    $exists = [bool](Get-TaskXml)
    Assert-Installer (-not $exists) 'affirmative uninstall deletes only the isolated task through shipping cleanup code'
    Assert-Installer (-not (Test-Path -LiteralPath $protectedParent)) 'affirmative uninstall removes the protected copy and its now-empty folders'
    Assert-Installer (-not (Test-Path -LiteralPath $installedExe) -and -not (Test-Path -LiteralPath $uninstaller)) 'affirmative uninstall removes application and uninstaller'
    $installed = $false

    # A same-name task that fails the ownership check (edited by the user or
    # another program) must neither be modified nor block uninstall. Enable
    # startup for a fresh install through the shipping manager, then change
    # the task's action arguments; it is registered disabled and
    # least-privilege so it can never run.
    $reinstall = Start-Process (Join-Path $root 'fixture-setup.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=en', ('/DIR="{0}"' -f $installDir)) -Wait -PassThru
    Assert-Installer ($reinstall.ExitCode -eq 0) 'reinstall for the foreign-task case completes'
    $installed = $true
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $manager = $type.GetConstructor($flags, $null, [type[]]@([string], [string], [string], [string]), $null).Invoke([object[]]@([string]$installedExe, [string]$sid, [string]$taskName, [string]$protectedRoot))
    try {
        $expected = $type.GetMethod('Read', $flags).Invoke($manager, $null)
        $type.GetMethod('Enable', $flags).Invoke($manager, [object[]]@($expected))
    } finally { $manager.Dispose() }
    Assert-Installer ((Get-TaskCommand) -eq $protectedExe -and (Test-Path -LiteralPath $protectedExe)) 'reinstalled copy enables startup with its protected copy'
    $foreignXml = (Get-TaskXml).Replace('<Arguments>--autostart</Arguments>', '<Arguments>--not-power-meter</Arguments>').Replace('HighestAvailable', 'LeastPrivilege').Replace('<Enabled>true</Enabled>', '<Enabled>false</Enabled>')
    $folder.RegisterTask($taskName, $foreignXml, 6, $sid, $null, 3, $null) | Out-Null
    $foreignXml = Get-TaskXml
    Assert-Installer ($foreignXml -match '--not-power-meter') 'same-name task now has a shape this program never registers'
    Remove-Item -LiteralPath $receipt -ErrorAction SilentlyContinue
    $foreignCode = Start-Uninstall 6 'foreign.log'
    Assert-Installer ($foreignCode -eq 0 -and (Test-Path -LiteralPath $receipt)) 'uninstall runs cleanup and completes despite a foreign same-name task'
    Assert-Installer ((Get-TaskXml) -eq $foreignXml) 'uninstall leaves the foreign same-name task unchanged'
    Assert-Installer (-not (Test-Path -LiteralPath $protectedParent)) 'uninstall still removes this installation''s protected copy and empty folders'
    Assert-Installer (-not (Test-Path -LiteralPath $installedExe) -and -not (Test-Path -LiteralPath $uninstaller)) 'uninstall with a foreign task removes application and uninstaller'
    $installed = $false
    Write-Host 'Real Inno migration/refresh, cancel/failure/accept and foreign-task lifecycle passed.'
}
finally {
    if ($activeUninstaller -and -not $activeUninstaller.HasExited) {
        & "$env:WINDIR\System32\taskkill.exe" /PID $activeUninstaller.Id /T /F | Out-Null
    }
    foreach ($ownedProcessId in $uninstallProcessIds) {
        $remaining = Get-CimInstance Win32_Process -Filter "ProcessId=$ownedProcessId" -ErrorAction SilentlyContinue
        if ($remaining -and $remaining.CommandLine -and $remaining.CommandLine.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Stop-Process -Id $ownedProcessId -Force -ErrorAction SilentlyContinue
        }
    }
    if (Test-Path -LiteralPath $failureFlag) { Remove-Item -LiteralPath $failureFlag -Force }
    if ($installed -and (Test-Path -LiteralPath $uninstaller)) {
        $cleanup = Start-Process $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -PassThru
        if (-not $cleanup.WaitForExit(10000)) {
            & "$env:WINDIR\System32\taskkill.exe" /PID $cleanup.Id /T /F | Out-Null
            Write-Warning 'Isolated uninstaller cleanup timed out.'
        } elseif ($cleanup.ExitCode -ne 0) {
            Write-Warning "Isolated uninstaller cleanup returned $($cleanup.ExitCode)"
        }
    }
    try { $folder.DeleteTask($taskName, 0) } catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        if ($cause.HResult -ne -2147024894) { throw }
    }
    if (Test-Path -LiteralPath $protectedParent) { Remove-Item -LiteralPath $protectedParent -Recurse -Force }
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    if (Test-Path -LiteralPath $registryPath) { Remove-Item -LiteralPath $registryPath -Recurse -Force }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
