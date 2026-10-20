#define AppName "Pointer Studio"
#define AppVersion "1.0.0"
#define AppPublisher "Pointer Studio"
#define AppExeName "CursorMaster.exe"
#define AppIcon "..\Assets\PointerStudio.ico"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{811876B7-122B-4709-B59B-B3411D2B5D9C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Pointer Studio
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=PointerStudioSetup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=classic
DisableWelcomePage=no
LanguageDetectionMethod=none
UsePreviousLanguage=no
WizardImageFile=Assets\Welcome.bmp
WizardSmallImageFile=Assets\Small.bmp

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "danish"; MessagesFile: "compiler:Languages\Danish.isl"

[CustomMessages]
english.CreateDesktopShortcut=Create a desktop shortcut
english.AdditionalShortcuts=Additional shortcuts:
english.LaunchApp=Launch Pointer Studio
russian.CreateDesktopShortcut=Создать ярлык на рабочем столе
russian.AdditionalShortcuts=Дополнительные ярлыки:
russian.LaunchApp=Запустить Pointer Studio
japanese.CreateDesktopShortcut=デスクトップにショートカットを作成
japanese.AdditionalShortcuts=追加のショートカット:
japanese.LaunchApp=Pointer Studio を起動
danish.CreateDesktopShortcut=Opret en genvej på skrivebordet
danish.AdditionalShortcuts=Yderligere genveje:
danish.LaunchApp=Start Pointer Studio

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; GroupDescription: "{cm:AdditionalShortcuts}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Assets\Welcome.bmp"; Flags: dontcopy
Source: "Assets\Finished.bmp"; Flags: dontcopy
Source: "Assets\Small.bmp"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent

[Code]
procedure InitializeWizard;
begin
  ExtractTemporaryFile('Welcome.bmp');
  ExtractTemporaryFile('Finished.bmp');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    WizardForm.WizardBitmapImage.Bitmap.LoadFromFile(
      ExpandConstant('{tmp}\Welcome.bmp'))
  else if CurPageID = wpFinished then
    WizardForm.WizardBitmapImage.Bitmap.LoadFromFile(
      ExpandConstant('{tmp}\Finished.bmp'));
end;
