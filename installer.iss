#define MyAppName "Lian Li LCD Template Editor"
#ifndef MyAppVersion
  #define MyAppVersion "2.5"
#endif
#define MyAppPublisher "Ozgur Yalcin"
#define MyAppURL "https://github.com/ozgurce/LianliThemeEditor"
#define MyAppExeName "LianLiThemeEditor.exe"

[Setup]
AppId={{5E2A5D54-E8B6-4E25-8BBE-9A2EA2FA3781}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\LianLiThemeEditor
DisableProgramGroupPage=yes
OutputDir=bin_build\installer
OutputBaseFilename=LianLiThemeEditorSetup
SetupIconFile=editor.ico
WizardImageFile=bin_build\installer\wizard.bmp
WizardSmallImageFile=bin_build\installer\wizard_small.bmp
Compression=none
SolidCompression=no
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
turkish.FinishedLabelNoIcons=Kurulum bilgisayarınıza [name] yüklemeyi tamamladı. Uygulama gerektiğinde Windows tarafından yönetici yetkisi isteyecektir.
turkish.FinishedLabel=Kurulum bilgisayarınıza [name] yüklemeyi tamamladı. Uygulama masaüstündeki kısayolundan veya başlat menüsünden başlatılabilir. Uygulama gerektiğinde Windows tarafından yönetici yetkisi isteyecektir.
english.FinishedLabelNoIcons=Setup has finished installing [name] on your computer. Windows will request administrator privileges when the application starts.
english.FinishedLabel=Setup has finished installing [name] on your computer. The application may be launched from the installed shortcuts. Windows will request administrator privileges when the application starts.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "bin_build\Release\net10.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
