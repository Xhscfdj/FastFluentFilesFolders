#define MyAppName "FastFluentFilesFolders"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "XhunjiWare"
#define MyAppExeName "FastFluentFilesFolders.exe"
#ifndef DestnationArch
    #define DestnationArch "x64"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}          
DefaultGroupName={#MyAppName}

OutputDir=Output           
OutputBaseFilename={#MyAppName}_{#MyAppVersion}_Setup_{#DestnationArch}

Compression=lzma
SolidCompression=yes

PrivilegesRequired=admin


ArchitecturesInstallIn64BitMode=x64compatible

Uninstallable=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[CustomMessages]
chinesesimplified.TaskDesktopIcon=创建桌面快捷方式
chinesesimplified.TaskGroup=附加任务:
chinesesimplified.TaskStartMenuIcon=创建开始菜单快捷方式

english.TaskDesktopIcon=Create a desktop shortcut
english.TaskGroup=Additional tasks:
english.TaskStartMenuIcon=Create a startmenu icon

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:TaskGroup}"
Name: "startmenuicon"; Description: "{cm:TaskStartMenuIcon}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked

[Files]
Source: "C:\Users\aaron\source\repos\LRS\FastFluentFilesFolders\publish_{#DestnationArch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;

end;