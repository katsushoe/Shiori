#ifndef AppVersion
  #error AppVersion must be defined.
#endif
#ifndef SourceDirectory
  #error SourceDirectory must be defined.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must be defined.
#endif

#define AppGuid "{{76842C4A-707D-4B1E-A544-C21F909FF959}"

[Setup]
AppId={#AppGuid}
AppName=Shiori
AppVersion={#AppVersion}
AppPublisher=Shiori contributors
AppPublisherURL=https://github.com/katsushoe/Shiori
AppSupportURL=https://github.com/katsushoe/Shiori/issues
AppUpdatesURL=https://github.com/katsushoe/Shiori/releases
DefaultDirName={localappdata}\Programs\Shiori
DefaultGroupName=Shiori
DisableProgramGroupPage=yes
LicenseFile={#SourceDirectory}\LICENSE
OutputDir={#OutputDirectory}
OutputBaseFilename=shiori-v{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
UninstallDisplayIcon={app}\bin\shiori.exe
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: "addtopath"; Description: "Add Shiori to the current user's PATH"; Flags: checkedonce

[Dirs]
Name: "{app}\config"; Flags: uninsneveruninstall
Name: "{app}\logs"; Flags: uninsneveruninstall
Name: "{app}\data"; Flags: uninsneveruninstall

[Files]
Source: "{#SourceDirectory}\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDirectory}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Shiori documentation"; Filename: "https://github.com/katsushoe/Shiori#readme"
Name: "{group}\Uninstall Shiori"; Filename: "{uninstallexe}"

[Code]
function NormalizedPathEntry(Value: string): string;
begin
  Result := RemoveBackslashUnlessRoot(Lowercase(ExpandConstant(Value)));
end;

function PathContains(Entry: string): Boolean;
var
  ExistingPath: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', ExistingPath) then
  begin
    Result := False;
    Exit;
  end;
  Result := Pos(';' + NormalizedPathEntry(Entry) + ';',
    ';' + Lowercase(ExistingPath) + ';') > 0;
end;

procedure AddToPath(Entry: string);
var
  ExistingPath: string;
begin
  if PathContains(Entry) then
    Exit;
  RegQueryStringValue(HKCU, 'Environment', 'Path', ExistingPath);
  if (ExistingPath <> '') and (ExistingPath[Length(ExistingPath)] <> ';') then
    ExistingPath := ExistingPath + ';';
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', ExistingPath + Entry);
end;

procedure RemoveFromPath(Entry: string);
var
  ExistingPath: string;
  PaddedPath: string;
  Target: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', ExistingPath) then
    Exit;
  PaddedPath := ';' + ExistingPath + ';';
  Target := ';' + Entry + ';';
  StringChangeEx(PaddedPath, Target, ';', False);
  if (Length(PaddedPath) > 0) and (PaddedPath[1] = ';') then
    Delete(PaddedPath, 1, 1);
  if (Length(PaddedPath) > 0) and (PaddedPath[Length(PaddedPath)] = ';') then
    Delete(PaddedPath, Length(PaddedPath), 1);
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', PaddedPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}\bin'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveFromPath(ExpandConstant('{app}\bin'));
end;
