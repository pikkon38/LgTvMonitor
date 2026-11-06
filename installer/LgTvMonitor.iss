; LG TV Monitor installer (Inno Setup 6). Built by CI; see .github/workflows/build.yml.
; Installs for all users, because the "turn the TV on at startup" task runs as SYSTEM before anyone signs in.
; The in-app updater downloads this installer from the latest GitHub release and runs it with /SILENT.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7E3C4B1A-5D2F-4A8E-9C61-2B7F0D4E8A93}
AppName=LG TV Monitor
AppVersion={#AppVersion}
AppVerName=LG TV Monitor {#AppVersion}
AppPublisher=pikkon38
AppPublisherURL=https://github.com/pikkon38/LgTvMonitor
AppUpdatesURL=https://github.com/pikkon38/LgTvMonitor/releases
DefaultDirName={autopf}\LgTvMonitor
DefaultGroupName=LG TV Monitor
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename=LgTvMonitor-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\LgTvMonitor.exe
UninstallDisplayName=LG TV Monitor
SetupIconFile=..\src\LgTvMonitor\LgTvMonitor.ico
; The running app is stopped in PrepareToInstall below, so Restart Manager isn't needed.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Dirs]
; Settings, pairing key and log. Users can write here so the tray app can save settings that the SYSTEM startup task reads.
Name: "{commonappdata}\LgTvMonitor"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "..\publish\LgTvMonitor.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\LG TV Monitor"; Filename: "{app}\LgTvMonitor.exe"
Name: "{group}\Uninstall LG TV Monitor"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LG TV Monitor"; Filename: "{app}\LgTvMonitor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LgTvMonitor.exe"; Parameters: "--register-boot-task"; StatusMsg: "Setting up the startup task..."; Flags: runhidden waituntilterminated
; Reopens the app as the signed-in user (not as admin), also after a silent update.
Filename: "{app}\LgTvMonitor.exe"; Description: "Open LG TV Monitor"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM LgTvMonitor.exe"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{app}\LgTvMonitor.exe"; Parameters: "--unregister-boot-task"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveBootTask"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  { Stop a running copy (tray app) so its exe can be replaced. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM LgTvMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
  Result := '';
end;
