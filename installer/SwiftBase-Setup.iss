; -----------------------------------------------------------------------------
; SwiftBase — professional Inno Setup installer
; Build with: ISCC.exe installer\SwiftBase-Setup.iss
; -----------------------------------------------------------------------------

#define MyAppName "SwiftBase"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "The L House"
#define MyAppExeName "SwiftBase.exe"

[Setup]
; Unique, stable identity so upgrades and uninstalls target the same product.
AppId={{B06AF034-8D36-4E82-910F-B4B3C4DDFAE4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright © The L House

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DirExistsWarning=no

; The published app is a self-contained win-x64 single-file executable.
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

; Admin install by default, but allow a per-user install when desired.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Ask to close a running SwiftBase before replacing/uninstalling files.
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

; Branding and output.
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=SwiftBase-Setup

; Setup executable metadata.
VersionInfoVersion=1.0.0.0
VersionInfoCompany=The L House
VersionInfoDescription=SwiftBase Setup
VersionInfoProductName=SwiftBase
VersionInfoProductVersion=1.0.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "program"; Description: "SwiftBase application"; Types: full compact custom; Flags: fixed
Name: "docs"; Description: "README and usage notes"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Single-file publish output. Debug symbols are intentionally excluded.
Source: "..\artifacts\SwiftBase\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"; Components: program
Source: "..\README.md"; DestDir: "{app}\Documentation"; Flags: ignoreversion; Components: docs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "Launch SwiftBase"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Comment: "Uninstall SwiftBase"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "Launch SwiftBase"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,SwiftBase}"; Flags: nowait postinstall skipifsilent
