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

[Setup]
AppId={{FDDC9FC9-109E-4B41-AE4A-BA30420295D0}
AppName=Battery Charge Meter
AppVersion={#AppVersion}
AppPublisher=dzshzx
AppPublisherURL=https://github.com/dzshzx/battery-charge-meter
AppSupportURL=https://github.com/dzshzx/battery-charge-meter/issues
DefaultDirName={localappdata}\Programs\Battery Charge Meter
DefaultGroupName=Battery Charge Meter
DisableProgramGroupPage=yes
LicenseFile={#LicensePath}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
UninstallDisplayIcon={app}\BatteryChargeMeter.exe

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "BatteryChargeMeter.exe"; Flags: ignoreversion
Source: "{#NoticePath}"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion
Source: "{#LicensePath}"; DestDir: "{app}"; DestName: "LICENSE.LGPL-2.1.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Battery Charge Meter"; Filename: "{app}\BatteryChargeMeter.exe"

[Run]
Filename: "{app}\BatteryChargeMeter.exe"; Description: "Launch Battery Charge Meter (requests administrator access)"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
  ExePath: String;
begin
  Result := True;
  ExePath := ExpandConstant('{app}\BatteryChargeMeter.exe');
  if FileExists(ExePath) then
  begin
    if not Exec(ExePath, '--remove-autostart', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := False
    else if ExitCode <> 0 then
      Result := False;
    if not Result then
      MsgBox('Unable to remove this installation''s logon task. Close Battery Charge Meter, disable its logon startup setting as administrator, then retry uninstall.', mbError, MB_OK);
  end;
end;
