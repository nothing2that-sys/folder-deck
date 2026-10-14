# FolderDeck — 1회용 창 상주 단축키(B안) · 1회용 창에서 앵커 빼기 (2026-10-14 2차)

작성 Claude · **실행 코웍(Claude Code)** · 성격 **구현 회차(5단계)**
저장소 `<repository-root>`
앞 회차: `doc/ScratchFavorites_2026-10-14_instructions.md` · 보고 `doc/ScratchFavorites_2026-10-14_report.md`

---

## §0 커밋 (JH 가 직접 실행 — 코웍에 넘기기 전)

앞 회차 코드가 아직 미커밋이다. **그 위에 얹지 않는다** — 먼저 앞 회차를 닫고, 이 지시서를 올린다.

```
git add src tests installer doc/ScratchFavorites_2026-10-14_report.md
git commit -m "Scratch window, shared favorites, layout presets, toolbar menu (2026-10-14)"
git add doc/ScratchAgent_2026-10-14_instructions.md
git commit -m "doc: instructions for resident hotkey agent and anchor-free scratch window"
```

`doc/USER_MANUAL.md` 는 무관한 미추적 파일이다. 같이 올리지 마라.

---

## §1 코웍에게 — 이 저장소의 규칙

앞 회차 §1 과 같다. 요지만 다시 적는다.

- **커밋하지 않는다.** 금지 명령 `git add -A` · `-a` · `git stash` · `git checkout --` · `git restore`.
- 범위 밖은 고치지 않고 보고문 「범위 밖 발견」에 적는다.
- 단계마다 빌드 + 전체 시험, 통과 수를 적는다. 앞 단계가 초록일 때만 다음 단계로 간다.
- 새 창·이벤트 걸린 컨트롤 → STA 생성 시험(`StaWpfTestSupport` — 이제 실물 `App` 리소스를 싣는다).
  이벤트 걸린 프로퍼티에 XAML 기본값을 주지 않는다.
- **버전을 올리지 않는다.**
- **기존 시험의 기대값을 바꿔야 할 때**: 이 지시서가 **명시한 변경의 직접 결과**(아래 §5 각 단계에
  「기대값이 바뀐다」고 적은 자리)면 고치고 보고문에 적는다. 그 밖이면 멈춘다(§7-4). 앞 회차에서
  §7-4 가 너무 넓어 지시서 스스로와 부딪쳤던 것을 고친 문구다.
- **개발 빌드를 실제 `%APPDATA%` 로 띄우지 않는다.** 설정 판이 5 라 설치본과 엇갈린다. 실물 실행
  확인은 시험(임시 루트) 또는 JH 손확인으로 한다.
- 영구 진단 로그를 남기지 않는다(앞 라운드 JH 반려). 측정용 계측은 측정 뒤 지우고 값만 보고문에 남긴다.

---

## §2 배경

앞 회차 손확인 결과(JH, 2026-10-14):

- 공유 즐겨찾기 반영 — **됨.**
- .lnk 바로 가기 키 변경 — **바뀌긴 하는데 뜨는 데 너무 느리다.**
  - 시작 메뉴에서 같은 바로가기를 클릭하면 **빠르다** → 앱 시작이 아니라 탐색기의 .lnk 단축키 처리가 느린 것.
  - 참고 실측(코웍): 설치본 `FolderDeck.exe --scratch` 직접 실행 → 창 핸들까지 670 · 654 · 1608 ms.
- 1회용 창의 「앵커 밖」 표시가 애매하다 — 닫으면 끝나는 창에 앵커가 왜 있나.

JH 결정(2026-10-14):

1. **B안** — 앱이 상주하며 전역 단축키를 직접 받는다(`RegisterHotKey`). .lnk 단축키는 버린다.
2. **1회용 창에서 앵커를 뺀다.** 일반 작업 관리는 그대로다.

---

## §3 확정 결정 (이 회차에서 바꾸지 않는다)

| 항목 | 값 | 근거 |
|---|---|---|
| 상주 형태 | 같은 실행 파일의 새 모드 `FolderDeck.exe --agent` | 설치·업데이트가 파일 하나로 끝난다. 별도 exe 는 배포물과 판 맞추기가 둘이 된다 |
| 상주 인스턴스 | 하나. 뮤텍스 `Local\FolderDeck.Agent`. 이미 있으면 두 번째 `--agent` 는 조용히 끝난다 | 단축키를 두 프로세스가 다투면 한쪽은 등록에 실패한다 |
| 단축키 수신 | 숨은 창(`HwndSource`) + `RegisterHotKey`(`MOD_NOREPEAT` 포함) | 공개 API 중 유일하게 즉시 받는 길이다 |
| 1회용 창이 뜨는 곳 | **상주 프로세스 안**에서 새 `MainWindow` 를 연다 | 프로세스 시작 비용이 없어진다 — B안을 고른 이유 그 자체 |
| 상주 프로세스의 종료 방식 | `ShutdownMode.OnExplicitShutdown` | 마지막 1회용 창을 닫아도 상주는 남아야 한다 |
| 트레이 아이콘 | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`) | WPF 에 트레이가 없다. P/Invoke 로 직접 짜는 것보다 검증된 부품이다 |
| 트레이 메뉴 | 「1회용 창 열기 (단축키)」 · 「작업 관리 열기」 · 구분선 · 「상주 끝내기」 | 「작업 관리 열기」는 기존 `SelfLauncher` 로 **새 프로세스** `--launcher` 를 띄운다 — 상주 프로세스에는 1회용 창만 산다 |
| `--scratch` (시작 메뉴 바로가기) | 상주가 살아 있으면 상주에게 신호만 보내고 끝난다. 없으면 지금처럼 자기 프로세스에서 연다 | 시작 메뉴 경로도 빨라지고, 상주가 꺼져 있어도 동작한다 |
| .lnk 단축키 | **버린다.** `ScratchShortcut`·`IScratchShortcut`·시작 때 동기화·`.iss` 의 `HotKey` 를 지운다 | 탐색기와 상주가 같은 키를 받으면 창이 두 번 뜬다. 재설치가 .lnk 를 다시 만들 때 키 없이 만들어지므로 미리보기 설치본의 키도 지워진다 |
| 단축키 규칙 | 앞 회차 `ScratchHotkey` 규칙 그대로(`Ctrl+Alt(+Shift)` + A–Z/0–9/F1–F12) | `RegisterHotKey` 는 더 넓게 받지만 규칙을 바꾸면 시험·UI 를 다시 짜야 한다 — 이번엔 안 넓힌다 |
| 로그인 시 자동 시작 | 설정 `AgentAutoStart`(기본 **켬**). 값은 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 의 `FolderDeck.Agent` = `"<exe>" --agent` | 켜 두지 않으면 재부팅 뒤 단축키가 죽어 있다. 사용자 단위 설치라 HKCU 면 권한이 필요 없다 |
| 설정 판 | **5 그대로** — `AgentAutoStart` 를 v5 이력 줄에 함께 적는다 | v5 는 아직 정식 배포되지 않았다(미리보기 설치본뿐). 그 파일엔 슬롯이 없어 기본값(켬)으로 읽힌다 |
| 설정 변경 반영 | 상주는 `settings.json` 을 감시하다 바뀌면 **다시 읽어 같은 인스턴스에 덮어쓰고**, 단축키가 바뀌었으면 다시 등록한다 | 설정 창은 어느 프로세스에서든 열릴 수 있다. 상주가 오래 들고 있는 낡은 설정을 나중에 저장하면 다른 창의 변경을 되돌린다 |
| 1회용 창의 앵커 | 1회용 창에서만 감춘다(§5 A1). 일반 작업 관리는 한 글자도 안 바뀐다 | JH 결정 2 |

---

## §4 시작 게이트

§0 커밋 뒤, 저장소 루트에서 `git -C <루트>` 로 잰다. 어긋나면 멈춘다.

| 항목 | 기대값 |
|---|---|
| `git status --porcelain -u -- src tests installer` | 빈 출력 |
| `git log -1 --format=%s` | `doc: instructions for resident hotkey agent and anchor-free scratch window` |
| `git log -2 --format=%s` 의 둘째 줄 | `Scratch window, shared favorites, layout presets, toolbar menu (2026-10-14)` |

`HEAD:src`·`HEAD:tests` 트리 해시는 §0 커밋이 만들므로 미리 적을 수 없다 — **재서 기록만** 한다.
시작 전 `dotnet test FolderDeck.slnx` 기준값을 적는다(앞 회차 끝: Core 400 · App 983 이어야 한다 —
다르면 멈춘다).

---

## §5 과제

### A1. 1회용 창에서 앵커 빼기

`MainViewModel.IsScratch` 일 때만 바뀐다. 패널에는 `CreateTile` 에서 플래그를 물려준다(예:
`FolderPanelViewModel.HidesAnchor`, init 전용). **일반 작업 관리의 동작·화면은 그대로**다.

| 자리 | 지금 | 1회용 창에서 |
|---|---|---|
| 「앵커 밖」 글자 (`FolderPanelView.xaml`, `Text="앵커 밖"`) | `IsAwayFromAnchor` 면 보임 | 감춘다. 표시용 속성(예: `ShowsAwayFromAnchor = IsAwayFromAnchor && !HidesAnchor`)을 따로 두고 XAML 은 그것만 본다 |
| ⌂ 버튼 (`AutomationProperties.Name="앵커로 복귀"`) + `Alt+Home` | 앵커로 | 버튼 감춤 · `Alt+Home` 무동작 |
| 「지금 보고 있는 폴더」 메뉴 「이 폴더를 앵커로 지정」 | 있음 | 감춤 |
| 항목 우클릭(셸 메뉴) 「이 폴더를 앵커로 지정」 | 폴더에 있음 | 넣지 않는다 — `ShellMenuPolicy.AppItems` 에 인자를 **기본값 있는 뒤쪽**으로 더해(예: `includeSetAnchor = true`) 기존 호출·시험을 안 깬다 |
| 브레드크럼 (`BuildBreadcrumbs`) | 앵커 아래면 「앵커이름 › 하위」로 접음 | 언제나 **드라이브 뿌리부터 전체 경로**(지금 「앵커 밖」 갈래와 같은 모양) |
| 즐겨찾기 클릭 (`OpenFavoriteAsync`) | 대상 칸에 폴더가 있으면 임시 이동 | 대상 칸이 비었든 아니든 **그 칸의 폴더를 바꾼다** — 앞 회차의 빈 칸 갈래(메모리 등록 + `RotationSlot` = 칸 번호 + `ShowFolderAsync`)를 그대로 탄다 |
| 왼쪽 폴더 목록 안내문 (`MainWindow.xaml`, 「재클릭 = 앵커로 복귀」) | 그대로 | 1회용 창에서는 「앵커」 낱말이 없는 문구로 |

**건드리지 말 것** — `FolderPanelViewModel.GoToAnchorAsync` 의 동작 자체. `ShowFolderAsync`(같은 폴더
재클릭)·`InitializeAsync` 가 내부에서 부른다. 막을 것은 **사용자 진입점**(버튼·키)뿐이다.

**전수 확인** — 시작 전에 `src/FolderDeck.App` 에서 `앵커` 가 화면 글자(XAML `Text`/`Header`/`ToolTip`
· C# 의 `Inform`/`Warn`/`Reject` 문자열)로 나오는 자리를 전부 뽑아 보고문에 표로 남기고, 각 자리를
「1회용에서 감춤 / 그대로 둠(이유)」으로 가른다. 위 표에 없는 자리가 나오면 같은 원칙(1회용 창에서
사용자에게 「앵커」가 안 보인다)으로 처리하고 표에 적는다.

**시험**
- 1회용 VM: 하위 폴더로 들어가도 `ShowsAwayFromAnchor` 거짓 · 브레드크럼 첫 조각이 드라이브 뿌리.
- 1회용 VM: 폴더가 떠 있는 칸에 즐겨찾기 → 그 칸 `Entry.Path` 가 즐겨찾기 경로(임시 이동이 아님).
- 일반 VM: 위 셋이 **지금과 같다**(기존 시험이 이미 보는 것은 다시 짜지 않아도 된다 — 어느 기존 시험이
  지키는지 이름을 적어라).
- `ShellMenuPolicy.AppItems(isDirectory: true, includeSetAnchor: false)` 에 `SetAnchor` 없음.
- 1회용 `MainWindow` STA 생성(앞 회차 시험이 있으면 그것이 계속 통과하는 것으로 갈음).

### A2. 상주 골격 (`--agent`)

- `CommandLineOptions` — `--agent`, `--agent-exit`. 다른 인자와 섞이면 기존 `Error` 관례.
- `InstanceNames` — 상주 뮤텍스·파이프 이름(`WorkspaceMutex`/`WorkspacePipe` 관례).
- `App.OnStartup`
  - `--agent`: 뮤텍스를 못 잡으면 **조용히 끝낸다**. 잡으면 `ShutdownMode = OnExplicitShutdown`,
    트레이 아이콘, 신호 수신, (A3) 단축키 등록. 창은 띄우지 않는다.
  - `--agent-exit`: 상주에게 끝내라는 신호를 보내고 끝난다(상주가 없으면 그냥 끝난다).
- 신호 — 기존 `ActivationSignal`/`ActivationListener` 는 페이로드(`ActivatePayload = "activate"`)를 싣는다.
  같은 길로 「1회용 창 열기」·「끝내기」 두 페이로드를 한 파이프에 싣는다. 리스너가 페이로드를 가르지
  못하는 구조면 파이프를 둘로 나눠도 된다 — 어느 쪽으로 했는지와 이유를 적는다.
- 상주 안에서 1회용 창 열기 = 앞 회차 `ShowScratchWindow` 와 같은 일(메모리 `Workspace` + 
  `ScratchWorkspaceStore`)이되, `MainWindow`/`_launcherWindow` 같은 「프로세스에 창 하나」 전제 필드를
  건드리지 않게 분리한다. 여러 개를 동시에 연다.
- 「상주 끝내기」: 열린 1회용 창을 닫고(저장할 것이 없다) 트레이 아이콘을 치우고 `Shutdown()`.
- 상주 프로세스가 설정·즐겨찾기를 쓰는 길은 지금 창들과 같다(같은 `WorkspaceStore` · `FavoritesStore`).

**시험** — 명령줄 넷(`--agent` · `--agent-exit` · 섞임 오류 둘). 상주 자체(트레이·뮤텍스·신호)는
App 수준이라 자동 시험이 닿는 만큼만 — 최소한 「1회용 창 열기」를 맡는 코드를 `App` 밖 클래스(예:
`ScratchWindowHost`)로 빼서, 그 클래스로 1회용 창 둘을 STA 에서 열고 닫는 시험 하나.

### A3. 전역 단축키

- 숨은 `HwndSource` 에 `RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT [| MOD_SHIFT] | MOD_NOREPEAT, vk)`.
  수식키·가상 키는 `ScratchHotkey` 에서 꺼낸다 — 지금 `ToShortcutValue` 는 .lnk WORD 를 내므로, `RegisterHotKey`
  용 (modifiers, vk) 를 내는 순수 함수를 **Core 에 더하고** 시험한다. 「없음」이면 등록하지 않는다.
- `WM_HOTKEY` → 1회용 창 열기(A2 와 같은 길).
- **등록 실패**(다른 프로그램이 이미 씀)는 트레이 풍선 알림 한 줄로 알린다. 앞 회차에서 「겹치는지는 알아낼
  방법이 없다」고 적었던 것이 이제는 알 수 있다 — 설정 창 「1회용 창」 탭의 안내문도 맞게 고친다.
- 설정 감시 — 상주가 `settings.json` 을 `FileSystemWatcher` 로 보고(즐겨찾기 감시와 같은 디바운스 결),
  다시 읽어 **기존 `AppSettings` 인스턴스에 덮어쓴다**(예: `AppSettings.CopyFrom`, Core 에 두고 모든 속성을
  옮기는지 시험 — 속성이 늘었는데 복사에서 빠지면 상주가 그 값을 되돌린다. 리플렉션으로 「공개 쓰기 가능
  속성 전부」를 세어 비교하는 시험을 둔다). `ScratchHotkey` 가 바뀌었으면 해제 후 다시 등록.
- **.lnk 단축키 제거**
  - 지운다: `Services/ScratchShortcut.cs`(`IScratchShortcut`·`NullScratchShortcut`·COM 인터롭 전부),
    `App.OnStartup` 의 동기화, `MainViewModel` 의 `_scratchShortcut`·`ReportShortcutSyncFailure`·
    `ReportShortcutApplied` 와 설정 저장 때 `Apply` 부르는 자리, `SettingsEditRequest.ScratchShortcutInstalled`.
  - 그에 딸린 시험(`ScratchModeTests` 의 단축키 배선 셋 · 실물 .lnk 둘)은 **기대값이 바뀌는 자리**다 —
    지우거나 새 구조(설정값만 바뀌고 상주가 감시로 받음)에 맞게 다시 쓴다.
  - `installer/FolderDeck.iss` 의 「Folder Deck 1회용」 줄에서 `HotKey: ctrl+alt+f` 와 그 주석을 지운다.
    바로가기 자체는 남긴다(시작 메뉴에서 빠르게 뜬다는 것이 JH 실측).
- 설정 창 「1회용 창」 탭 문구: 단축키는 **상주 프로그램이 받는다**, 상주가 꺼져 있으면 안 먹는다,
  상주 상태(실행 중 / 꺼짐 — `InstanceSignals.IsLive` 로 판정)와 「지금 상주 시작」 버튼.

**시험** — (modifiers, vk) 순수 함수 · `CopyFrom` 전 속성 · 상주 단축키 등록 클래스를 숨은 창 없이 시험할
수 있게 등록/해제를 인터페이스 뒤로 빼고 가짜로 「바뀌면 해제 → 등록」 순서를 본다. 실제 `RegisterHotKey`
호출은 시험 하나로 실물 확인(등록 → 해제가 성공하는가; 그 키가 이미 이 PC 에서 쓰이고 있으면 실패가 정상이므로
쓰이지 않을 조합 — 예 `Ctrl+Alt+Shift+F11` — 을 쓴다).

### A4. 시작 메뉴 위임 · 자동 시작 · 설치 프로그램

- `--scratch`: 상주가 살아 있으면(`InstanceSignals.IsLive`) 「1회용 창 열기」 신호만 보내고 끝난다. 신호가
  실패하면 지금처럼 자기 프로세스에서 연다.
- `AppSettings.AgentAutoStart`(기본 `true`) — 설정 창 「1회용 창」 탭 체크박스. 저장할 때와 상주가 시작할 때
  Run 키를 설정값에 맞춘다(켬 → 값 쓰기, 끔 → 값 지우기). 값의 실행 파일 경로는 **지금 실행 중인 exe 경로**
  (`Environment.ProcessPath`)다. Run 키 읽고 쓰기는 인터페이스 뒤에 두고 가짜로 시험한다 — 시험이 실제 HKCU
  Run 키를 건드리면 안 된다.
- `installer/FolderDeck.iss`
  - 설치 끝에 상주 시작: `[Run]` 에 `--agent` (`Flags: nowait postinstall skipifsilent` 결 — 사용자가 끌 수 있게).
  - **업그레이드·제거 때 상주를 끝낸다** — 파일이 잡혀 있으면 덮어쓰기·삭제가 실패한다. `--agent-exit` 를
    설치 전(`[Code]` 의 `PrepareToInstall` 등)과 제거 전(`[UninstallRun]`)에 부르는 쪽을 먼저 시도한다.
  - 제거 때 Run 키 값을 지운다(`[Registry]` 의 `uninsdeletevalue` 또는 `[UninstallRun]` 에서 앱이 지움 —
    택한 쪽과 이유를 적는다).
  - ISCC 로 **scratchpad 출력**에 컴파일해 exit code 를 적는다(`artifacts\installer\` 를 덮지 않는다).

**시험** — `--scratch` 위임 갈래(상주 있음 → 신호만 · 신호 실패 → 자기 프로세스)를 판정 함수로 빼서 시험.
Run 키 맞추기(켬/끔/경로 바뀜)를 가짜 레지스트리로 시험.

### A5. 측정 (값만 남기고 계측은 지운다)

1. 상주에서 **단축키 → 1회용 창 `ContentRendered`** 까지(ms). 상주 시작 뒤 **첫 번째**와 **두 번째~다섯 번째**를
   따로 적는다. 숨은 창에 `WM_HOTKEY` 를 직접 보내는 식으로 재도 된다(실제 키 입력 경로와 다르다는 것을 적는다).
2. 비교용: 상주 없이 `--scratch` 새 프로세스 → 창 핸들까지(앞 회차 670 · 654 · 1608 ms 와 같은 방식).
3. 상주 프로세스의 작업 집합(Working Set) — 시작 직후 · 1회용 창 20번 열고 닫은 뒤(GC 후).
   많이 늘었으면(닫힌 창이 안 풀림) 「범위 밖 발견」에 적는다.

**측정은 임시 루트로 한다**(§1 — 실제 `%APPDATA%` 금지). 그게 안 되는 측정은 「재지 않았다」로 적는다.

**첫 번째가 두 번째보다 크게 느리면**(예: 500ms 이상 차이) 데우기(상주 시작 때 창 하나를 화면 밖에서 만들었다
닫기 등)를 **하지 말고** 값과 제안만 적는다 — JH 가 정한다.

---

## §6 비목표

- 단축키 규칙 넓히기(Win 키 · Ctrl+Shift 등).
- 일반 작업 관리 창을 상주 프로세스 안에서 열기.
- 일반 작업 관리의 앵커 변경.
- 1회용 창 「저장/승격」.
- 상주 프로세스 업데이트 알림·자동 재시작.
- 매뉴얼·릴리스 노트·버전.

---

## §7 정지 조건

1. 시작 게이트 어긋남 → 멈춘다.
2. 빌드 실패 → 한 단계 안에서 세 번까지. 그래도 안 되면 그 단계에서 멈추고 앞 단계 결과는 남긴다.
3. **`UseWindowsForms=true` 가 기존 코드에 이름 충돌(`Application`·`MessageBox`·`Clipboard` 등)을 일으켜,
   고치려면 기존 파일 여러 곳에 별칭·전체 이름을 넣어야 한다** → 다섯 파일을 넘으면 멈추고, 트레이를
   `Shell_NotifyIcon` P/Invoke 로 짜는 안과 비교해 적는다.
4. 기존 시험 기대값 변경이 §5 에 「기대값이 바뀐다」고 적힌 자리 밖에서 필요하다 → 멈춘다.
5. 일반 작업 관리의 화면·동작이 A1 때문에 바뀌어야만 한다 → 멈춘다.
6. 설치 프로그램이 업그레이드 때 상주를 끝내지 못한다(파일 잠김) → 시도한 것과 ISCC/설치 로그를 적고, 나머지
   A4 를 끝낸 뒤 보고한다. JH 가 정한다.
7. 실제 `RegisterHotKey` 시험이 이 PC 에서 등록 자체를 못 한다(정책 등) → 그 시험만 빼고 이유를 적는다.

---

## §8 산출물

- 코드·시험 변경(커밋하지 않는다). `git status --porcelain -u -- src tests installer` 출력 그대로.
- 보고문 `doc/ScratchAgent_2026-10-14_report.md`:
  - §1 시작 게이트 실측 · 기준 시험 수
  - §2 단계별(A1~A5): 한 일 · 새 시험 이름 · 빌드/시험 통과 수
  - §3 A1 의 「앵커」 노출 자리 전수 표
  - §4 A5 측정값(무엇을 어떤 방식으로 쟀는지 같이)
  - §5 지시서가 틀린 자리 · §6 범위 밖 발견 · §7 JH 손확인 목록
- 핸드오프는 쓰지 않는다.

---

## §9 JH 손확인 (참고용)

1. 새 설치본 설치 → 트레이에 FolderDeck 아이콘이 있는가. 설치 직후 `Ctrl+Alt+F` 로 1회용 창이 **바로** 뜨는가.
2. 연달아 다섯 번 눌러 창 다섯이 뜨는가. 다 닫아도 트레이 아이콘이 남는가.
3. 설정에서 단축키를 `Ctrl+Alt+G` 로 바꾸면 곧 새 키로 뜨고 옛 키는 안 먹는가(재시작 없이).
4. 다른 프로그램이 쓰는 키로 바꾸면 트레이 알림이 뜨는가.
5. 재부팅(또는 로그아웃·로그인) 뒤 단축키가 살아 있는가. 설정에서 자동 시작을 끄면 다음 로그인엔 상주가 없는가.
6. 트레이 「상주 끝내기」 뒤 단축키가 안 먹고, 시작 메뉴 「Folder Deck 1회용」은 여전히 뜨는가.
7. 업그레이드 설치가 상주가 떠 있는 상태에서 실패 없이 끝나는가. 제거 뒤 트레이 아이콘과 자동 시작이 남지 않는가.
8. 1회용 창: 하위 폴더로 들어가도 「앵커 밖」이 안 뜨고, 경로가 드라이브부터 보이는가. 폴더가 떠 있는 칸을
   클릭하고 즐겨찾기를 누르면 그 칸이 그 폴더로 바뀌는가. ⌂ 버튼과 「앵커로 지정」 메뉴가 안 보이는가.
9. 일반 작업 관리 창은 앵커 표시·메뉴가 예전 그대로인가.
10. (앞 회차 이월) 빈 칸을 클릭하면 파란 선이 그 칸으로 옮겨 가는가.
