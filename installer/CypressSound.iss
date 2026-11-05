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

[Icons]
Name: "{autoprograms}\Cypress Sound"; Filename: "{app}\CypressSound.exe"
Name: "{autodesktop}\Cypress Sound"; Filename: "{app}\CypressSound.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\{#ApoSetup}"; Parameters: "/S"; StatusMsg: "Installing the sound engine (Equalizer APO). In the window that opens, tick your speakers, TV and headphones, then click OK."; Flags: waituntilterminated; Check: ApoDownloaded
Filename: "{app}\CypressSound.exe"; Parameters: "--setup-apo-config"; StatusMsg: "Connecting Cypress Sound to the sound engine..."; Flags: waituntilterminated runhidden
Filename: "{app}\CypressSound.exe"; Description: "Start Cypress Sound"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im CypressSound.exe /f"; Flags: runhidden; RunOnceId: "KillApp"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Cypress Sound"; Flags: uninsdeletevalue dontcreatekey

[Code]
var
  ApoWasMissing: Boolean;
  ApoFetched: Boolean;
  DownloadPage: TDownloadWizardPage;

function ApoInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\EqualizerAPO');
end;

function NeedsApo(): Boolean;
begin
  Result := not ApoInstalled();
end;

function ApoDownloaded(): Boolean;
begin
  Result := ApoFetched;
end;

function InitializeSetup(): Boolean;
begin
  ApoWasMissing := not ApoInstalled();
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Getting the sound engine', 'Downloading Equalizer APO (free), which runs the EQ inside Windows.', nil);
end;

function TryDownload(Url: String): Boolean;
begin
  DownloadPage.Clear;
  DownloadPage.Add(Url, '{#ApoSetup}', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Result := True;
    except
      Log(GetExceptionMessage);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsApo() and not ApoFetched then
  begin
    ApoFetched := TryDownload('https://downloads.sourceforge.net/project/equalizerapo/1.4.2/{#ApoSetup}');
    if not ApoFetched then
      ApoFetched := TryDownload('https://sourceforge.net/projects/equalizerapo/files/1.4.2/{#ApoSetup}/download');
    if not ApoFetched then
      MsgBox('Equalizer APO could not be downloaded (are you online?). Cypress Sound will still install and switch outputs; ' +
             'the EQ starts working once you install Equalizer APO from sourceforge.net/projects/equalizerapo.', mbInformation, MB_OK);
  end;
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
