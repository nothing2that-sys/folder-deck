[Setup]
AppId=FolderDeck
AppName=Folder Deck
AppVersion=1.7.0
AppPublisher=Folder Deck
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

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]

Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Folder Deck"; Filename: "{app}\FolderDeck.exe"
