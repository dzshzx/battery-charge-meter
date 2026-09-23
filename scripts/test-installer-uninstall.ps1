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
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path ([IO.Path]::GetTempPath()) "bcm-installer-$id"
$installDir = Join-Path $root 'installed'
$installedExe = Join-Path $installDir 'PowerMeter.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$receipt = Join-Path $root 'cleanup-called.txt'
$failureFlag = Join-Path $root 'force-cleanup-failure'
$taskName = "BatteryChargeMeter.InstallerTest.$id"
$appId = "PowerMeterTest.$id"
$shortcutName = "Battery Charge Meter Installer Test $id"
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$shortcutName.lnk"
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
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
    $iss = Get-Content (Join-Path $repo 'installer\BatteryChargeMeter.iss') -Raw
    $iss = $iss.Replace('AppId={{FDDC9FC9-109E-4B41-AE4A-BA30420295D0}', "AppId=$appId")
    $iss = $iss.Replace('Name: "{autoprograms}\{cm:ApplicationName}";', ('Name: "{{autoprograms}}\{0}";' -f $shortcutName))
    Assert-Installer ($iss.Contains("AppId=$appId") -and $iss.Contains($shortcutName)) 'fixture has isolated installer identities'
    $issPath = Join-Path $root 'fixture.iss'
    $iss = '#define ChineseMessages "' + $repo + '\installer\Languages\ChineseSimplified.isl"' + [Environment]::NewLine + $iss
    Set-Content -LiteralPath $issPath -Value $iss -Encoding UTF8

    # The fixture entry point routes cleanup to the shipping manager with a
    # unique task name. It also injects a nonzero cleanup result for the fatal
    # failure case; no production test flag or real autostart task is needed.
    $code = @'
using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Diagnostics;
class InstallerCleanupFixture {
    static int Main(string[] args) {
        if (args.Length != 1 || args[0] != "--remove-autostart") return 2;
        File.AppendAllText(@"__RECEIPT__", "cleanup\r\n");
        if (File.Exists(@"__FAILURE__")) return 1;
        object manager = null;
        try {
            Assembly app = Assembly.LoadFile(@"__PAYLOAD__");
            Type type = app.GetType("BatteryChargeMeter.AutostartManager", true);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string sid = WindowsIdentity.GetCurrent().User.Value;
            manager = type.GetConstructor(flags, null, new Type[] {typeof(string), typeof(string), typeof(string)}, null)
                .Invoke(new object[] {Process.GetCurrentProcess().MainModule.FileName, sid, "__TASK__"});
            type.GetMethod("Disable", flags).Invoke(manager, null);
            return 0;
        } catch { return 1; }
        finally { if (manager != null) ((IDisposable)manager).Dispose(); }
    }
}
'@
    $code = $code.Replace('__RECEIPT__', $receipt.Replace('"', '""')).Replace('__FAILURE__', $failureFlag.Replace('"', '""')).Replace('__PAYLOAD__', $ExecutablePath.Replace('"', '""')).Replace('__TASK__', $taskName)
    $fixtureSource = Join-Path $root 'fixture.cs'
    $fixtureExe = Join-Path $root 'fixture.exe'
    Set-Content -LiteralPath $fixtureSource -Value $code -Encoding UTF8
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$fixtureExe" $fixtureSource
    if ($LASTEXITCODE -ne 0) { throw 'Cleanup fixture compilation failed.' }
    & $InstallerCompilerPath '/Q' '/DAppVersion=9.8.7' "/DSourceExe=$fixtureExe" "/DLegacyLauncher=$repo\dist\compat\BatteryChargeMeter.exe" "/DNoticePath=$repo\third_party\NOTICE.md" "/DLicensePath=$repo\third_party\LICENSE.LGPL-2.1.txt" "/DOutputDir=$root" '/DOutputBaseFilename=fixture-setup' $issPath
    if ($LASTEXITCODE -ne 0) { throw 'Real installer compilation failed.' }
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
    # Simulate an upgrade in the same AppId and directory. Its existing logon
    # task must remain byte-for-byte unchanged while its old action still works.
    Copy-Item -LiteralPath $fixtureExe -Destination $legacyExe
    $app = [Reflection.Assembly]::LoadFile($ExecutablePath)
    $type = $app.GetType('BatteryChargeMeter.AutostartManager', $true)
    $xml = $type.GetMethod('BuildXml', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@([string]$legacyExe, [string]$sid))
    # A disabled, least-privilege fixture is registrable by an ordinary CI
    # account and can never execute on login. Disable() still verifies its
    # production ownership marker, exact executable, SID and action arguments.
    $xml = $xml.Replace('HighestAvailable', 'LeastPrivilege').Replace('<Enabled>true</Enabled>', '<Enabled>false</Enabled>')
    $folder.RegisterTask($taskName, $xml, 2, $sid, $null, 3, $null) | Out-Null
    $before = $folder.GetTask($taskName).Xml
    $upgrade = Start-Process (Join-Path $root 'fixture-setup.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=zhCN', ('/DIR="{0}"' -f $installDir)) -Wait -PassThru
    Assert-Installer ($upgrade.ExitCode -eq 0) 'Chinese upgrade completes in the existing installation'
    Assert-Installer ((Get-ItemProperty $registryPath).DisplayName -eq '功率计') 'Chinese installer uses localized branding'
    Assert-Installer ([Diagnostics.FileVersionInfo]::GetVersionInfo($legacyExe).FileDescription -eq 'Power Meter legacy launcher') 'upgrade replaces the old executable with the compatibility launcher'
    Assert-Installer ($folder.GetTask($taskName).Xml -eq $before) 'upgrade preserves the legacy logon task and its policy'
    $aliasMethod = $type.GetMethod('IsCurrentCopy', [Reflection.BindingFlags]'Static,NonPublic')
    Assert-Installer ($aliasMethod.Invoke($null, [object[]]@([string]$legacyExe, [string]$installedExe))) 'new application recognizes its installed legacy launcher'
    Assert-Installer (-not $aliasMethod.Invoke($null, [object[]]@([string]$legacyExe, [string]$ExecutablePath))) 'another portable copy cannot claim the legacy task'
    Set-Content -LiteralPath $failureFlag -Value 'injected nonzero cleanup result'
    $legacyCleanup = Start-Process $legacyExe -ArgumentList '--remove-autostart' -Wait -PassThru
    Assert-Installer ($legacyCleanup.ExitCode -eq 1 -and (Test-Path -LiteralPath $receipt)) 'legacy launcher forwards cleanup and preserves its failure code'
    Remove-Item -LiteralPath $failureFlag, $receipt
    $cancelCode = Start-Uninstall 7 'cancel.log'
    Assert-Installer (-not (Test-Path -LiteralPath $receipt)) 'cancel never invokes startup cleanup'
    Assert-Installer ($folder.GetTask($taskName).Xml -eq $before) 'cancel preserves the actual task unchanged'
    Assert-Installer ((Test-Path -LiteralPath $installedExe) -and (Test-Path -LiteralPath $uninstaller)) 'cancel preserves installed executable and uninstaller'

    Set-Content -LiteralPath $failureFlag -Value 'injected nonzero cleanup result'
    $failureCode = Start-Uninstall 6 'failure.log'
    Assert-Installer ($failureCode -ne 0 -and (Test-Path -LiteralPath $receipt)) 'affirmative uninstall propagates cleanup failure'
    Assert-Installer ($folder.GetTask($taskName).Xml -eq $before) 'cleanup failure preserves the actual task'
    Assert-Installer ((Test-Path -LiteralPath $installedExe) -and (Test-Path -LiteralPath $uninstaller) -and (Test-Path -LiteralPath (Join-Path $installDir 'unins000.dat'))) 'fatal hook exception preserves application and uninstall data'
    Remove-Item -LiteralPath $failureFlag
    $acceptedCode = Start-Uninstall 6 'accept.log'
    Assert-Installer ($acceptedCode -eq 0) 'affirmative uninstall succeeds after cleanup succeeds'
    $exists = $true
    try { $folder.GetTask($taskName) | Out-Null } catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        if ($cause.HResult -ne -2147024894) { throw }
        $exists = $false
    }
    Assert-Installer (-not $exists) 'affirmative uninstall deletes only the isolated task through shipping cleanup code'
    Assert-Installer (-not (Test-Path -LiteralPath $installedExe) -and -not (Test-Path -LiteralPath $uninstaller)) 'affirmative uninstall removes application and uninstaller'
    $installed = $false
    Write-Host 'Real Inno cancel/failure/accept lifecycle passed.'
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
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    if (Test-Path -LiteralPath $registryPath) { Remove-Item -LiteralPath $registryPath -Recurse -Force }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
