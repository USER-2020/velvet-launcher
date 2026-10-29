#ifndef PublishDir
#define PublishDir "publish"
#endif
#ifndef AppVersion
#define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8B7CF1EA-7E33-4A0B-9F56-65D7D6B2B8A8}
AppName=VELVET Launcher
AppVersion={#AppVersion}
DefaultDirName={autopf}\VELVET Launcher
DefaultGroupName=VELVET Launcher
OutputDir=artifacts
OutputBaseFilename=VELVET-Launcher-windows-x64
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\Velvet.Launcher.App.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VELVET Launcher"; Filename: "{app}\Velvet.Launcher.App.exe"
Name: "{commondesktop}\VELVET Launcher"; Filename: "{app}\Velvet.Launcher.App.exe"

[Run]
Filename: "{app}\Velvet.Launcher.App.exe"; Description: "Abrir VELVET Launcher"; Flags: nowait postinstall skipifsilent
