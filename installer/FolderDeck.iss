; FolderDeck installer -- Phase 7 D47
; Per-user install. No elevation. Built by: ISCC.exe installer\FolderDeck.iss

[Setup]
AppId=FolderDeck
AppName=Folder Deck
AppVersion=1.4.0
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

; 앞선 판이 남긴 것을 먼저 비운다. {app} 은 이 앱 전용 폴더({localappdata}\FolderDeck\app)라
; 통째로 지워도 사용자 데이터가 걸리지 않는다 — 워크스페이스는 {userappdata}\FolderDeck 에 있다.
; 이것이 없으면 판이 바뀌며 사라진 파일이 설치 폴더에 영원히 남는다(1.0.0 의 *.pdb 둘이 실제로
; 그랬다 — 지금은 DebugType=none 이라 만들지도 않는데 설치본에는 있었다).
[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
; ignoreversion 이 반드시 있어야 한다. 없으면 Inno 는 파일의 버전 정보를 비교해
; "같거나 더 새것이 아니면 덮어쓰지 않는다". FolderDeck.Core.dll 은 csproj 에 <Version> 이
; 없어 어느 빌드에서나 1.0.0.0 이므로, 1.0.0 -> 1.1.0 설치에서 갈리지 않고 어제치가 남았다.
; 새 FolderDeck.dll + 옛 Core.dll 짝이 되어 시작 즉시 MissingMethodException 으로 죽었다
; (LeftLayoutSpec.LeftPanelCollapsed — Phase 8-E 가 넣은 것). 자체 포함 배포는 .NET 런타임
; DLL 판도 빌드 간 같을 수 있어 이 함정이 Core.dll 하나에 그치지 않는다.
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Folder Deck"; Filename: "{app}\FolderDeck.exe"
