; FolderDeck installer -- Phase 7 D47
; Per-user install. No elevation. Built by: ISCC.exe installer\FolderDeck.iss

[Setup]
AppId=FolderDeck
AppName=Folder Deck
AppVersion=1.0.0
AppPublisher=JH
DefaultDirName={localappdata}\FolderDeck\app
DefaultGroupName=Folder Deck
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=FolderDeck-Setup
SetupIconFile=..\src\FolderDeck.App\Assets\FolderDeck.ico
UninstallDisplayIcon={app}\FolderDeck.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Folder Deck"; Filename: "{app}\FolderDeck.exe"
