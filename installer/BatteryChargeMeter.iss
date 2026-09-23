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
function HasLegacyExecutable(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\BatteryChargeMeter.exe'));
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
    else if ExitCode <> 0 then
      RaiseException(CustomMessage('CleanupFailed'));
  end;
end;
