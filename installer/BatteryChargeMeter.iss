#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceExe
  #error SourceExe is required
#endif
#ifndef NoticePath
  #error NoticePath is required
#endif
#ifndef LicensePath
  #error LicensePath is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename is required
#endif
#ifndef ChineseMessages
  #define ChineseMessages AddBackslash(SourcePath) + "Languages\ChineseSimplified.isl"
#endif

[Setup]
AppId={{FDDC9FC9-109E-4B41-AE4A-BA30420295D0}
AppName={cm:ApplicationName}
AppVersion={#AppVersion}
AppPublisher=dzshzx
AppPublisherURL=https://github.com/dzshzx/battery-charge-meter
AppSupportURL=https://github.com/dzshzx/battery-charge-meter/issues
DefaultDirName={localappdata}\Programs\Power Meter
DefaultGroupName={cm:ApplicationName}
DisableProgramGroupPage=yes
LicenseFile={#LicensePath}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
UninstallDisplayIcon={app}\PowerMeter.exe
UninstallDisplayName={cm:ApplicationName}
#ifdef IconPath
SetupIconFile={#IconPath}
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhCN"; MessagesFile: "{#ChineseMessages}"

[CustomMessages]
en.ApplicationName=Power Meter
zhCN.ApplicationName=功率计
en.LaunchApplication=Launch Power Meter (requests administrator access)
zhCN.LaunchApplication=启动功率计（请求管理员权限）
en.CleanupLaunchFailed=Unable to start logon-task cleanup. Uninstall has been stopped; the application is still installed.
zhCN.CleanupLaunchFailed=无法启动自启任务清理。卸载已停止，应用仍然保留。
en.CleanupFailed=Unable to remove this installation's logon task. Uninstall has been stopped; disable its logon startup setting as administrator, then retry.
zhCN.CleanupFailed=无法删除此安装的自启任务。卸载已停止，请以管理员身份关闭开机自启后重试。
en.StartupCopyNotUpdated=Logon startup keeps running the previous version from its protected copy. Open Power Meter as administrator to update it.
zhCN.StartupCopyNotUpdated=开机自启仍运行受保护副本中的旧版本。以管理员身份打开功率计即可更新。
en.StartupDisarmed=Logon startup ran a file that standard processes can replace, so it has been turned off. Open Power Meter as administrator and enable it again.
zhCN.StartupDisarmed=开机自启运行的是普通权限可替换的文件，已将其关闭。请以管理员身份打开功率计后重新启用。
en.StartupDisarmFailed=Logon startup runs a file that standard processes can replace and could not be turned off. Open Power Meter as administrator and enable or disable its logon startup.
zhCN.StartupDisarmFailed=开机自启运行的是普通权限可替换的文件，且未能关闭。请以管理员身份打开功率计，重新启用或关闭开机自启。
en.StartupCopyRemains=The logon task was removed, but its protected copy under Program Files\Power Meter\autostart needs administrator access to delete. It no longer starts automatically.
zhCN.StartupCopyRemains=自启任务已删除，但 Program Files\Power Meter\autostart 下的受保护副本需要管理员权限才能删除。它不会再自动运行。

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "PowerMeter.exe"; Flags: ignoreversion
#ifdef LegacyLauncher
Source: "{#LegacyLauncher}"; DestDir: "{app}"; DestName: "BatteryChargeMeter.exe"; Flags: ignoreversion; Check: HasLegacyExecutable
#endif
Source: "{#NoticePath}"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion
Source: "{#LicensePath}"; DestDir: "{app}"; DestName: "LICENSE.LGPL-2.1.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{cm:ApplicationName}"; Filename: "{app}\PowerMeter.exe"

[Run]
Filename: "{app}\PowerMeter.exe"; Description: "{cm:LaunchApplication}"; Flags: nowait postinstall skipifsilent

[Code]
const
  { Exit codes of PowerMeter.exe --sync-autostart / --remove-autostart. }
  StartupStaleCopy = 3;
  StartupUnprotected = 4;
  StartupCopyNeedsElevation = 3;

function HasLegacyExecutable(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\BatteryChargeMeter.exe'));
end;

{ Runs the installed application with administrator rights. An elevated
  setup runs it directly; otherwise Windows shows one UAC prompt. }
function RunElevated(const ExePath, Params: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := ShellExec('runas', ExePath, Params, ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Needed, ExitCode: Integer;
  ExePath: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  { The logon task runs an administrator-only copy of the application. An
    upgrade refreshes that copy, and migrates a task left by an older version
    that still runs a user-writable file. Without elevation, an unprotected
    task is removed rather than left in place. }
  ExePath := ExpandConstant('{app}\PowerMeter.exe');
  if not Exec(ExePath, '--sync-autostart', ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, Needed) then
    Needed := 0;
  if ((Needed = StartupStaleCopy) or (Needed = StartupUnprotected))
    and not RunElevated(ExePath, '--sync-autostart') then
  begin
    if Needed = StartupStaleCopy then
      SuppressibleMsgBox(CustomMessage('StartupCopyNotUpdated'), mbInformation, MB_OK, IDOK)
    else if Exec(ExePath, '--remove-autostart', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode <> 1) then
      SuppressibleMsgBox(CustomMessage('StartupDisarmed'), mbInformation, MB_OK, IDOK)
    else
      SuppressibleMsgBox(CustomMessage('StartupDisarmFailed'), mbError, MB_OK, IDOK);
  end;
end;

procedure InitializeUninstallProgressForm();
var
  ExitCode: Integer;
  ExePath: String;
begin
  { Inno calls this after affirmative confirmation and before PerformUninstall.
    Setup.Uninstall.pas re-raises exceptions from this event as fatal, so a
    failed cleanup preserves the installed EXE and uninstall data. }
  ExePath := ExpandConstant('{app}\PowerMeter.exe');
  if FileExists(ExePath) then
  begin
    if not Exec(ExePath, '--remove-autostart', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException(CustomMessage('CleanupLaunchFailed'))
    else if ExitCode = StartupCopyNeedsElevation then
    begin
      { The task is already gone; only its inert protected copy remains. }
      if not RunElevated(ExePath, '--remove-autostart') then
        SuppressibleMsgBox(CustomMessage('StartupCopyRemains'), mbInformation, MB_OK, IDOK);
    end
    else if ExitCode <> 0 then
      RaiseException(CustomMessage('CleanupFailed'));
  end;
end;
