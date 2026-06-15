; Acuris Desktop - Windows installer (Inno Setup 6)
; Build: powershell -ExecutionPolicy Bypass -File build-installer.ps1

#define AppName "Acuris Desktop"
#define AppExe "AcurisDesktop.exe"
#define AppPublisher "Acuris"
#define AppUrl "https://github.com/utsavDoris/rfid"
#define SourceDir "..\publish\AcurisDesktop"
#define OutputDir "..\publish"

[Setup]
AppId={{A8F3C2E1-9B4D-4A6F-8E2C-D5RFID2026}}
AppName={#AppName}
AppVersion=1.0.0
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,install.ps1,uninstall.ps1,Install.bat,README-PUBLIC.txt"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\AcurisDesktop"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Release: Cardinal;
begin
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
  begin
    Result := 'Microsoft .NET Framework 4.8 is required.' + #13#10 +
      'Download: https://dotnet.microsoft.com/download/dotnet-framework/net48';
    Exit;
  end;
  if Release < 528040 then
  begin
    Result := '.NET Framework 4.8 or newer is required.' + #13#10 +
      'Download: https://dotnet.microsoft.com/download/dotnet-framework/net48';
    Exit;
  end;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteKeyIncludingSubkeys(HKEY_LOCAL_MACHINE,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AcurisDesktop');
end;
