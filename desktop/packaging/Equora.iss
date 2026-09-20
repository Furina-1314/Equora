#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the self-contained application payload
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
[Setup]
AppId={{FB4F5C09-DB55-40A6-9872-F407BEFBF65C}
AppName=衡序 Equora
AppVersion={#AppVersion}
AppPublisher=Equora
AppPublisherURL=https://github.com/Furina-1314/Equora
DefaultDirName={localappdata}\Programs\Equora
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Equora-v{#AppVersion}-win-x64-Setup
SetupIconFile={#PayloadDir}\Assets\Equora.ico
UninstallDisplayIcon={app}\Equora.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=衡序 Equora (EXE)
VersionInfoVersion=0.2.0.0
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,AppxManifest.xml,AppxBlockMap.xml,AppxSignature.p7x,[Content_Types].xml"
[Icons]
Name: "{userprograms}\衡序 Equora"; Filename: "{app}\Equora.App.exe"
Name: "{userdesktop}\衡序 Equora"; Filename: "{app}\Equora.App.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Equora.App.exe"; Description: "Launch Equora"; Flags: nowait postinstall skipifsilent
