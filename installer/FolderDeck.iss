; FolderDeck installer -- Phase 7 D47
; Per-user install. No elevation. Built by: ISCC.exe installer\FolderDeck.iss

[Setup]
AppId=FolderDeck
AppName=Folder Deck
AppVersion=1.8.0
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
; 1회용 창(2026-10-14 S4 · 2차). 여기서는 바로 가기 키를 넣지 않는다 — 기본은 상주 프로그램(--agent)이
; RegisterHotKey 로 받는다. 사용자가 설정에서 「상주 프로그램으로 받기」를 끄면 그때 앱이 이 바로가기에 키를 쓴다.
; 둘 다 켜지면 같은 키에 창이 두 번 뜨므로, 설치가 키를 미리 넣지 않는다. 시작 메뉴에서 누르면 상주에게 맡긴다.
Name: "{group}\Folder Deck 1회용"; Filename: "{app}\FolderDeck.exe"; Parameters: "--scratch"

; 상주 프로그램의 로그인 시 자동 시작(2026-10-14 2차). 값은 앱이 설정에 맞춰 쓰고 지운다(HotkeyRouting) —
; 설치는 만들지 않고(ValueType: none) 제거 때 지우기만 한다. 앱이 남긴 값이 제거 뒤에 죽은 경로를 가리키지 않게.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FolderDeck.Agent"; Flags: uninsdeletevalue dontcreatekey

; 설치가 끝나면 상주를 띄운다 — 체크를 풀 수 있다. 설정이 상주를 끄라고 하면 상주는 스스로 바로 끝난다.
[Run]
Filename: "{app}\FolderDeck.exe"; Parameters: "--agent"; Description: "1회용 창 단축키 상주 프로그램 시작"; Flags: nowait postinstall skipifsilent

; 제거 전에 상주를 끝낸다 — 떠 있으면 FolderDeck.exe 가 잡혀 지워지지 않는다. --agent-exit 는 상주가 끝날 때까지
; 잠깐 기다린 뒤 돌아온다.
[UninstallRun]
Filename: "{app}\FolderDeck.exe"; Parameters: "--agent-exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopAgent"

[Code]
// 업그레이드 설치 전에 떠 있는 상주를 끝낸다(2026-10-14 2차) — 안 그러면 FolderDeck.exe 를 덮지 못한다.
// **상주 뮤텍스가 있을 때만** 부른다. 상주가 없는 옛 판(1.7.0 이하 · 미리보기)은 --agent-exit 를 몰라 「알 수 없는
// 인자」로 선택 창을 띄우고 기다린다 — 그러면 ewWaitUntilTerminated 가 설치를 멈춘다. 옛 판은 이 뮤텍스를 만들지
// 않으므로 이 검사가 그 경우를 거른다. 이름은 앱의 InstanceNames.AgentMutex 와 같아야 한다.
const
  AgentMutex = 'Local\FolderDeck.Agent';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\FolderDeck.exe');
  if FileExists(Exe) and CheckForMutexes(AgentMutex) then
    Exec(Exe, '--agent-exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
