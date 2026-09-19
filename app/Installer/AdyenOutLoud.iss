; Inno Setup script for the Windows installer attached to GitHub releases (see .github/workflows/release.yml).
; Compile with: ISCC /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<folder> AdyenOutLoud.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E4A4F2B-3C7D-4F1E-9B8A-2D5C1E7F0A93}
AppName=Adyen Out Loud
AppVersion={#AppVersion}
AppPublisher=Adyen Out Loud contributors
AppPublisherURL=https://github.com/astiskala/adyen-out-loud
DefaultDirName={autopf}\Adyen Out Loud
DefaultGroupName=Adyen Out Loud
DisableProgramGroupPage=yes
; Installs per user by default, so no administrator rights are needed.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=AdyenOutLoud-{#AppVersion}-windows-x64-setup
UninstallDisplayIcon={app}\AdyenOutLoud.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Adyen Out Loud"; Filename: "{app}\AdyenOutLoud.exe"
Name: "{autodesktop}\Adyen Out Loud"; Filename: "{app}\AdyenOutLoud.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AdyenOutLoud.exe"; Description: "{cm:LaunchProgram,Adyen Out Loud}"; Flags: nowait postinstall skipifsilent
