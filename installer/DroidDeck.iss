; DroidDeck Windows Installer Script (Inno Setup 6)
; Bundles DroidDeck.exe + Scrcpy + ADB into a standalone zero-dependency setup wizard.

#define MyAppName "DroidDeck"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Yulia2233"
#define MyAppURL "https://github.com/Yulia2233/DroidDeck"
#define MyAppExeName "DroidDeck.exe"

[Setup]
AppId={{8E9F4A7B-5C1D-4C6B-9E2E-2D9A1B7C3F01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=DroidDeck-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\desktop\build\DroidDeck.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\desktop\build\bin\scrcpy\*"; DestDir: "{app}\bin\scrcpy"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\bin\scrcpy\adb.exe"; Parameters: "kill-server"; Flags: runhidden waituntilterminated; RunOnceId: "KillAdbServer"
