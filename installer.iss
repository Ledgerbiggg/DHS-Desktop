; ============================================================
; Dsh 安装包脚本 (Inno Setup 6)
; 用法: iscc installer.iss [/DMyAppVersion=0.1.1]
; ============================================================

#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif

#define MyAppName "Dsh"
#define MyAppPublisher "Dsh"
#define MyAppExeName "Dsh.exe"

[Setup]
; 安装包/卸载程序的唯一标识
AppId={{B4C5D6E7-3A4F-5B6C-8D7E9F0A1B2C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
SetupIconFile=Dsh\Assets\deepseek-dark_48x48.ico
OutputDir=package
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64os
UninstallFilesDir={app}\Uninstall

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; 将 dotnet publish 产物整体打包
Source: "_publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "额外快捷方式:"

[Run]
; 安装完成后可选启动
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
