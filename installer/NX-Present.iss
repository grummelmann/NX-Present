; NX-Present Windows installer. Build with Inno Setup 7.
#ifndef AppVersion
 #define AppVersion "0.4.0"
#endif
#ifndef AppDir
 #define AppDir "..\artifacts\publish"
#endif
#ifndef ReleaseDir
 #define ReleaseDir "..\artifacts\release"
#endif
#ifndef ProjectUrl
 #define ProjectUrl "https://github.com/nexoGroups/NX-Present"
#endif

[Setup]
AppId={{B16827AC-3BD9-4EB7-9C33-285050840CC5}
AppName=NX-Present
AppVersion={#AppVersion}
AppVerName=NX-Present {#AppVersion}
AppPublisher=NX-Present
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases/latest
VersionInfoDescription=NX-Present Setup
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\NX-Present
DefaultGroupName=NX-Present
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
AppMutex=Local\NX-Present-Controller
SetupMutex=NX-Present-Setup
OutputDir={#ReleaseDir}
OutputBaseFilename=NX-Present-Setup-{#AppVersion}-x64
SetupIconFile=..\src\NX-Present\Assets\nx-present.ico
UninstallDisplayIcon={app}\NX-Present.exe
LicenseFile=..\LICENSE.txt
InfoBeforeFile=install-notes.txt
Compression=lzma2
SolidCompression=yes
LZMANumBlockThreads=2
WizardStyle=modern dark includetitlebar
WizardSizePercent=110
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName=NX-Present

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Verknüpfungen:"

[InstallDelete]
; The video engine's plugin catalog only matches the plugin files of its own version. Files that a newer version no longer ships must not stay behind.
Type: filesandordirs; Name: "{app}\libvlc"

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE.txt"; DestDir: "{app}"; DestName: "LICENSE-NX-Present.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\Hilfe.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\examples\Demo.nxshow"; DestDir: "{app}\examples"; Flags: ignoreversion
Source: "dependencies\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\NX-Present"; Filename: "{app}\NX-Present.exe"; WorkingDir: "{app}"
Name: "{group}\NX-Present Hilfe"; Filename: "{app}\Hilfe.html"
Name: "{autodesktop}\NX-Present"; Filename: "{app}\NX-Present.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\NX-Present.exe"; Description: "NX-Present starten"; Flags: nowait postinstall skipifsilent
; An update started by NX-Present itself runs silently and passes /relaunch=1: the new version opens right after the installation.
Filename: "{app}\NX-Present.exe"; WorkingDir: "{app}"; Flags: nowait; Check: RelaunchRequested

[Code]
function OpenProcess(Access: Cardinal; Inherit: Boolean; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

{ An update starts this setup from the running program and passes its process id (/waitpid=...).
  The files of the old version can only be replaced once that process has ended. }
procedure WaitForRunningProgram;
var
  ProcessId: Integer;
  Handle: THandle;
begin
  ProcessId := StrToIntDef(ExpandConstant('{param:waitpid|0}'), 0);
  if ProcessId <= 0 then exit;
  Handle := OpenProcess($00100000, False, ProcessId);
  if Handle = 0 then begin
    Log('Process ' + IntToStr(ProcessId) + ' has already ended.');
    exit;
  end;
  Log('Waiting for process ' + IntToStr(ProcessId) + ' to end.');
  WaitForSingleObject(Handle, 30000);
  CloseHandle(Handle);
end;

function InitializeSetup: Boolean;
begin
  WaitForRunningProgram;
  Result := True;
end;

function ValidRuntimeVersion(const Root: Integer): Boolean;
var Version: String;
begin
  Result := RegQueryStringValue(Root,
    'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function WebView2Installed: Boolean;
begin
  Result := ValidRuntimeVersion(HKLM32) or ValidRuntimeVersion(HKLM64) or
    ValidRuntimeVersion(HKCU32) or ValidRuntimeVersion(HKCU64);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if WebView2Installed then begin
    Log('Microsoft Edge WebView2 is already installed.');
    exit;
  end;
  Log('Installing the official Microsoft Edge WebView2 bootstrapper.');
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
    '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := 'Microsoft Edge WebView2 konnte nicht gestartet werden. Bitte die WebView2-Laufzeit von Microsoft installieren und dieses Setup erneut starten.';
    exit;
  end;
  NeedsRestart := ExitCode = 3010;
  if ((ExitCode <> 0) and (ExitCode <> 3010)) or not WebView2Installed then
    Result := 'Microsoft Edge WebView2 konnte nicht installiert werden (Code ' +
      IntToStr(ExitCode) + '). Prüfe die Internetverbindung oder installiere die WebView2-Laufzeit direkt von Microsoft. NX-Present-Setup danach erneut starten.';
end;
