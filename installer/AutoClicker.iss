#define AppName "Auto Clicker"
#define AppVersion "3.0.1"
#define AppPublisher "Auto Clicker"
#ifndef SourceExe
  #define SourceExe "..\dist\installer-staging\AutoClicker.exe"
#endif

[Setup]
AppId={{D7BDCA76-9D5D-4DD4-A1CE-6F09894627E6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Auto Clicker
DefaultGroupName=Auto Clicker
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=AutoClicker-v3.0.1-Setup
SetupIconFile=..\src\WinClicker\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AutoClicker.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=3.0.1.0
VersionInfoProductName=Auto Clicker
VersionInfoDescription=Auto Clicker installer

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\installer-staging\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\installer-staging\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\installer-staging\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\installer-staging\Apache-2.0.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Auto Clicker"; Filename: "{app}\AutoClicker.exe"
Name: "{autodesktop}\Auto Clicker"; Filename: "{app}\AutoClicker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AutoClicker.exe"; Description: "{cm:LaunchProgram,Auto Clicker}"; Flags: nowait postinstall skipifsilent
