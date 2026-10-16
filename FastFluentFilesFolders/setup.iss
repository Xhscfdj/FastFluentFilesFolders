#define MyAppName "FastFluentFilesFolders"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "XhunjiWare"
#define MyAppExeName "FastFluentFilesFolders.exe"
#ifndef DestinationArch
    #define DestinationArch "x64"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}          
DefaultGroupName={#MyAppName}

OutputDir=Output\{#DestinationArch}
OutputBaseFilename={#MyAppName}_Setup_{#DestinationArch}

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

; 覆盖安装前先清掉历史遗留的 MSIX/AppX 打包产物与旧构建残留。
; 这些文件不在发布输出里，[Files] 永远不会覆盖它们；但 WinUI 3 的 XAML
; 资源加载器会优先读到它们（旧 resources.pri / 松散的 Views\*.xbf），
; 导致新版程序集去加载旧版 XAML，启动即崩溃：
;   XamlParseException: Cannot create instance of type '...Views.MiddleFilesView'
[InstallDelete]
Type: files; Name: "{app}\resources.pri"
Type: files; Name: "{app}\*.xbf"
Type: files; Name: "{app}\AppxManifest.xml"
Type: files; Name: "{app}\*.build.appxrecipe"
Type: filesandordirs; Name: "{app}\AppX"
Type: filesandordirs; Name: "{app}\Views"
Type: filesandordirs; Name: "{app}\UserControls"
Type: filesandordirs; Name: "{app}\publish"
Type: filesandordirs; Name: "{app}\runtimes"
; 旧发布残留的其它架构运行时文件
Type: files; Name: "{app}\Microsoft.DiaSymReader.Native.arm64.dll"
Type: files; Name: "{app}\mscordaccore_arm64_arm64_*.dll"

[Files]
Source: "C:\Users\aaron\source\repos\LRS\FastFluentFilesFolders\publish_{#DestinationArch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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