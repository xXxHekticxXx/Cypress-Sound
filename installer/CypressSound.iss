; Cypress Sound installer (Inno Setup 6). Built by .github/workflows/build.yml.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define ApoSetup "EqualizerAPO-x64-1.4.2.exe"

[Setup]
AppId={{6B1D3C52-8F0A-4C61-9C1E-2E7C0B5A9D41}
AppName=Cypress Sound
AppVersion={#AppVersion}
AppVerName=Cypress Sound {#AppVersion}
AppPublisher=Cypress Works
AppPublisherURL=https://github.com/xXxHekticxXx/Cypress-Sound
DefaultDirName={autopf}\Cypress Works\Cypress Sound
DefaultGroupName=Cypress Sound
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\out
OutputBaseFilename=Cypress-Sound-Setup
SetupIconFile=..\src\CypressSound\Assets\CypressSound.ico
UninstallDisplayIcon={app}\CypressSound.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE.txt

[Tasks]
Name: "desktopicon"; Description: "Desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\third_party\{#ApoSetup}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsApo

[Icons]
Name: "{autoprograms}\Cypress Sound"; Filename: "{app}\CypressSound.exe"
Name: "{autodesktop}\Cypress Sound"; Filename: "{app}\CypressSound.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\{#ApoSetup}"; Parameters: "/S"; StatusMsg: "Installing the sound engine (Equalizer APO). In the window that opens, tick your speakers, TV and headphones, then click OK."; Flags: waituntilterminated; Check: NeedsApo
Filename: "{app}\CypressSound.exe"; Parameters: "--setup-apo-config"; StatusMsg: "Connecting Cypress Sound to the sound engine..."; Flags: waituntilterminated runhidden
Filename: "{app}\CypressSound.exe"; Description: "Start Cypress Sound"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im CypressSound.exe /f"; Flags: runhidden; RunOnceId: "KillApp"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Cypress Sound"; Flags: uninsdeletevalue dontcreatekey

[Code]
var
  ApoWasMissing: Boolean;

function ApoInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\EqualizerAPO');
end;

function NeedsApo(): Boolean;
begin
  Result := not ApoInstalled();
end;

function InitializeSetup(): Boolean;
begin
  ApoWasMissing := not ApoInstalled();
  Result := True;
end;

function NeedRestart(): Boolean;
begin
  { Windows only loads the sound engine into a device at startup. }
  Result := ApoWasMissing;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir, Backup: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKLM64, 'SOFTWARE\EqualizerAPO', 'ConfigPath', Dir) then
    begin
      Backup := AddBackslash(Dir) + 'config.before-cypress-sound.txt';
      if FileExists(Backup) then
      begin
        DeleteFile(AddBackslash(Dir) + 'config.txt');
        RenameFile(Backup, AddBackslash(Dir) + 'config.txt');
      end;
      DeleteFile(AddBackslash(Dir) + 'cypress-sound.txt');
    end;
  end;
end;
