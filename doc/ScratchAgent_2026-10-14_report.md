# FolderDeck — 1회용 창 상주 단축키(B안) · 1회용 창에서 앵커 빼기 — 보고 (2026-10-14 2차)

지시서 `doc/ScratchAgent_2026-10-14_instructions.md` · 실행 코웍(Claude Code) · **코드는 커밋하지 않았다.**
§0 의 두 커밋은 JH 의 채팅 요청("커밋하고 바로 진행해줘")으로 내가 쳤다:
`Scratch window, shared favorites, …` → `doc: instructions for resident hotkey agent …`(`83fa2d6`).

---

## §1 시작 게이트

| 항목 | 기대값 | 실측 |
|---|---|---|
| `git status --porcelain -u -- src tests installer` | 빈 출력 | 빈 출력 |
| `git log -1 --format=%s` | `doc: instructions for resident hotkey agent and anchor-free scratch window` | 같음 |
| `git log -2` 둘째 줄 | `Scratch window, shared favorites, layout presets, toolbar menu (2026-10-14)` | 같음 |
| 기준 시험 | Core 400 · App 983 | Core 400 · App 983 |

기록만: `HEAD` `83fa2d6384e3b44e506c39fbfb5076670e735cb4` · `HEAD:src` `ee86bfad5a7fab415cce9f53f012f32a7ca9577a` ·
`HEAD:tests` `79a70a9ead407b0d9b9e551b74983cc4fb05e88a`.

---

## §2 회차 중 JH 결정으로 바뀐 것

A1 을 시작한 직후 JH 가 물었다: 「상주 프로세스로 단축키를 받는 방식을 사용자가 선택하게 할 수 없나?」
코웍이 체크박스 하나로 둘 중 하나만 켜는 안을 냈고, JH 가 「그렇게 해줘, 진행해」로 확정했다.
지시서(커밋됨)는 고치지 않고 여기 적는다.

| 지시서 | 바뀐 것 |
|---|---|
| §3 「.lnk 단축키 — **버린다**」, A3 「`ScratchShortcut`·`IScratchShortcut`·시작 때 동기화를 지운다」 | **남긴다.** 사용자가 「상주 프로그램으로 받기」를 끄면 그 바로가기가 키를 받는다(상주 없음 · 느림). 실물 .lnk 시험 둘도 그대로 산다 |
| §3·A4 설정 `AgentAutoStart` | **`UseResidentAgent` 하나로 합쳤다**(기본 켬). 켬 = 바로가기 키 지움 + Run 키 씀 + 상주가 `RegisterHotKey` 로 받음. 끔 = 바로가기 키 = 설정값 + Run 키 지움 + 상주 스스로 끝남. 둘을 따로 두면 「상주는 안 쓰는데 자동 시작만 켜짐」 같은 엇갈린 조합이 생긴다 |
| A3 「설정 창에 상주 상태와 「지금 상주 시작」 버튼」 | 버튼은 두지 않았다. **저장할 때** 상주를 쓰는데 꺼져 있으면 띄운다. 상태는 탭의 안내문(실행 중 / 꺼짐 — 저장하면 띄움)으로 보인다 |
| 설치 프로그램 `HotKey` 제거 | 그대로 지웠다. 끔 쪽을 고르면 앱이 그때 바로가기에 키를 쓴다 |

---

## §3 단계별 결과

| 단계 | Core | App | 빌드 실패(그 단계 안) |
|---|---|---|---|
| 기준 | 400 | 983 | — |
| A1 앵커 빼기 | 400 | 989 | 0 |
| A2~A4 상주 · 단축키 · 위임 · 설치 | 408 | 1007 | 0 (시험 쪽 컴파일 오류 1 — 지시서가 「기대값이 바뀐다」고 적은 시험 생성자 인자. 다시 써서 해결) |
| A5 측정 뒤 측정 시험 삭제 | 408 | 1007 | — |

A2~A4 를 한 덩어리로 짰다. 상주 골격, 단축키, 위임이 같은 클래스들(`AgentRuntime`·`HotkeyRouting`)을 나눠
쓰기 때문이다. 끝에서 전체 시험을 두 번 연속 돌려 둘 다 전부 통과였다.

### A1 — 1회용 창에서 앵커 빼기

- `FolderPanelViewModel` 에 init 전용 `HidesAnchor` 를 더했다. `MainViewModel.CreateTile` 이 `IsScratch` 를 물려준다.
- 표시용 속성 `ShowsAwayFromAnchor`(= `IsAwayFromAnchor && !HidesAnchor`) · `ShowsAnchorActions` 를 더했다.
  `HasStatusLine` 은 `ShowsAwayFromAnchor || IsLoading` 이다. 1회용에서 「앵커 밖」 줄이 빈 채로 자리만 차지하지 않게 했다.
- `GoToAnchorFromUserAsync` 를 새로 두었다. ⌂ 버튼과 `Alt+Home` 은 이것을 부르고, 1회용이면 아무 일도 안 한다.
  **`GoToAnchorAsync` 자체는 그대로다**(같은 폴더 재클릭 · 처음 띄우기가 안에서 부른다).
- `BuildBreadcrumbs(…, fullPath)` — 1회용이면 늘 드라이브 뿌리부터 낸다. 「앵커 밖」 갈래를
  `FullPathBreadcrumbs` 로 빼서 둘이 같은 코드를 쓴다.
- 셸 메뉴 `ShellMenuPolicy.AppItems(…, includeSetAnchor = true)`, `ShellItemMenu.Show(…, includeSetAnchor = true)` —
  기본값 있는 뒤쪽 인자라 기존 호출과 시험이 안 바뀐다.
- 즐겨찾기: 1회용이고 대상이 순환 칸이면, 폴더가 떠 있어도 빈 칸 갈래(메모리 등록 + `ShowFolderAsync`)를 탄다.
  즉 그 칸의 폴더 자체를 바꾼다.
- 왼쪽 목록 안내문은 `MainViewModel.FolderListHint` 로 옮겼다. 1회용에서는 「재클릭 = 처음 폴더로」다. 일반 창의
  글은 예전 그대로다(시험이 「앵커로 복귀」를 확인한다).
- 새 시험(`ScratchModeTests`) 여섯:
  - `GoingIntoASubfolderShowsNoAnchorWordsInScratch`
  - `ANormalWorkspaceStillShowsTheAnchor`
  - `TheHomeKeyDoesNothingInScratchButStillWorksInANormalWorkspace`
  - `AFavoriteReplacesTheFolderOfAnOccupiedScratchTile`
  - `TheShellMenuCanLeaveOutSetAnchor`
  - `TheFolderListHintDropsTheAnchorWordInScratch`
- 일반 작업 관리의 앵커 동작은 위 「일반」 시험과 기존 시험이 지킨다.
  - `AnchorNavigationTests` 전체
  - `ShellMenuPolicyTests.FolderGetsAllSixInOrder`(`SetAnchor` 포함 목록)
  - `FavoritesTests.ClickingAFavoriteMovesTheTargetTileWithoutTouchingTheWorkspace`(일반 창은 여전히 임시 이동)

### A2 — 상주 골격

- `--agent` · `--agent-exit` 는 **홀로** 써야 한다. 섞이면 `Error` 를 내고 둘 다 버린다.
- `InstanceNames.AgentMutex`(`Local\FolderDeck.Agent`) · `AgentPipe`.
- **신호:** 기존 리스너에 「본문 여럿을 가르는」 생성자를 더하고, 한 파이프에 `scratch` · `exit` 두 본문을 실었다.
  기존 단일 본문 생성자는 새 생성자로 넘긴다(작업 관리 「앞으로」 신호는 그대로).
  `ActivationSignal.TrySend(pipe, payload)` 를 더했다(포그라운드 권한도 같이 넘김).
- **`ScratchWindowHost`** — 1회용 창을 열고(메모리 작업 관리 + `ScratchWorkspaceStore`), 세고, 모두 닫는다.
  `App.MainWindow`·`LastWorkspaceId` 를 건드리지 않는다.
- **`App.xaml.cs`**
  - 뷰모델·창 만들기를 `BuildWorkspaceWindow` 로 빼서 `ShowWorkspaceWindow` 와 상주가 같이 쓴다.
  - `--agent-exit` 는 설정·바로가기·Run 키를 건드리기 전에 신호를 보낸다. 그 뒤 상주 뮤텍스가 사라질 때까지
    최대 5초 기다리고 끝난다.
- **`AgentRuntime`**
  - 뮤텍스(이미 있으면 false → 프로세스 끝) · `ShutdownMode.OnExplicitShutdown` · 신호 수신 · 숨은 창 단축키 ·
    트레이를 갖췄다.
  - 트레이 메뉴는 「1회용 창 열기 (단축키)」 · 「작업 관리 열기」(새 프로세스 `--launcher`) · 「상주 끝내기」이고,
    아이콘 더블클릭도 1회용 창을 연다.
  - `UseResidentAgent` 가 꺼져 있으면 아예 서지 않는다.
- **트레이(WinForms `NotifyIcon`)**
  - `UseWindowsForms=true` 로 켰다. 이 조합은 `System.Windows.Forms`·`System.Drawing` 을 전역 using 으로 깔아
    WPF 이름과 부딪칠 수 있다.
  - csproj 의 `<Using Remove=…>` 두 줄로 걷어 냈고, **기존 파일은 하나도 안 고쳤다** — 정지 조건 3 에 해당하지 않는다.
  - 새 코드는 `Forms = System.Windows.Forms` 별칭으로 부른다.
- 새 시험
  - 명령줄 다섯(`TheAgentSwitchesAreRead` 둘, `TheAgentSwitchesMustStandAlone` 셋)
  - `TheHostOpensSeveralScratchWindowsAndClosesThemAll` — STA 에서 실물 `MainWindow` 둘을 열고, 하나 닫고, 모두
    닫은 뒤 `workspaces\` 파일 0 을 확인

### A3 — 전역 단축키

- **Core**
  - `ScratchHotkey.ToRegistration` — `RegisterHotKey` 의 (수식키, 가상 키)를 낸다. 수식키 비트 배치가 .lnk 의
    `HOTKEYF_*` 와 달라(ALT 1 · CONTROL 2 · SHIFT 4) 따로 변환하고, `MOD_NOREPEAT` 를 늘 붙인다.
  - `AppSettings.UseResidentAgent`(기본 켬) — v5 이력 줄에 함께 적고 판은 그대로 두었다.
  - `AppSettings.CopyFrom`.
- **`GlobalHotkey`**(메시지 전용 숨은 창 + `RegisterHotKey`) · **`AgentHotkeyBinder`**(바뀌었을 때만 해제 → 등록,
  실패면 등록 없음 + 이유).
  - 등록 실패(`ERROR_HOTKEY_ALREADY_REGISTERED` 1409)는 「다른 프로그램이 이미 그 단축키를 쓰고 있다」로
    트레이 풍선에 띄운다.
- **설정 감시** — 상주가 `settings.json` 을 감시하고, 250 ms 모아 다시 읽어 `CopyFrom` 으로 **같은 인스턴스에**
  덮어쓴다. 상주가 꺼졌으면 `Exit()`, 아니면 단축키를 다시 맞춘다.
- **`HotkeyRouting`**(`IScratchShortcut` + `IRunKey` + `IAgentControl`) — §2 의 표대로 바로가기 키와 Run 키를 한쪽만
  켠다.
  - **모든 시작 경로**(`--agent-exit` 제외)와 **설정 저장 때** 부른다. 설정을 못 쓰는 실행에서는 안 부른다.
  - Run 값은 `"<exe>" --agent` 이고, 같으면 다시 쓰지 않는다.
- **설정 저장 순서**: 단축키나 받는 쪽이 바뀌면 `_store.Flush()` 로 **설정을 먼저 파일에 쓴 뒤** 라우팅하고, 상주가
  꺼져 있으면 띄운다. 새 상주는 시작할 때 파일을 읽는다 — 디바운스가 아직 안 썼으면 「상주 끔」을 읽고 바로 끝난다.
  시험 `TheSettingsFileIsWrittenBeforeTheAgentIsStarted` 가 이것을 잰다.
- 설정 창 「1회용 창」 탭에 체크박스 「상주 프로그램으로 받기」(바인딩만)를 두고, 상황별 안내문 넷을 보인다.
  앞 회차의 「겹치는지는 알아낼 방법이 없다」 문장은 지웠다 — 이제 상주가 알아낸다.
- 새 시험
  - **Core**
    - `AppSettingsCopyTests` — **공개 쓰기 가능 속성 전부를 리플렉션으로** 비교한다.
    - 시험용 값이 모든 속성에서 기본값과 다른지 확인한다. 새 속성을 더하면 여기서 먼저 깨진다.
    - 컬렉션 복사, 구파일 기본값.
    - `ToRegistration` 넷.
  - **App — 설정 배선**(`RecordingRouting` 가짜로 다시 씀 — 지시서가 기대값이 바뀐다고 적은 자리)
    - 키 바뀜 → 라우팅 + 꺼진 상주 띄움
    - 설정이 상주 띄우기 전에 파일에 써짐
    - 살아 있으면 안 띄움
    - 안 바뀌면 아무것도 안 함
    - 끔 → 바로가기 쪽, 아무것도 안 띄움, 파일에 끔이 써짐
    - 끔 + 바로가기 없음 안내
  - **App — `HotkeyRouting` 실물 규칙**(가짜 바로가기 · 가짜 Run 키, **실제 HKCU 는 안 건드린다**)
    - 켬: 바로가기 키 「없음」 · Run 값 따옴표 경로 · 같은 값 두 번째는 다시 안 씀
    - 끔: 바로가기 키 = 설정값 · Run 값 지움
  - **App — `AgentHotkeyBinder`**(가짜 등록기)
    - 호출 순서: `unregister → register 4003 46` → 같은 값 무동작 → `unregister → register 4007 47` →
      「없음」이면 `unregister`
    - 실패면 등록 없음
  - **실물 `RegisterHotKey`** — `Ctrl+Alt+Shift+F11` 로 등록 → 숨은 창에 `WM_HOTKEY` 를 보내 콜백 1회 →
    해제 → 다시 등록 성공. 이미 쓰이는 키면 「이미」 갈래로 끝나게 짜 두었다.
    **이 PC 에서 그 조합이 비어 있는지 따로 확인했다**(PowerShell 로 같은 조합 등록 → True) — 시험이 실물 경로를 탔다.

### A4 — 시작 메뉴 위임 · 자동 시작 · 설치 프로그램

- `ScratchLaunch.DelegatedToAgent(useAgent, agent)` — 상주를 쓰고, 살아 있고, 신호가 닿았을 때만 맡기고 끝난다.
  상주를 안 쓰거나 꺼져 있으면 신호를 아예 보내지 않는다. 시험 넷.
- 자동 시작(Run 키)은 §2 대로 `UseResidentAgent` 를 따른다(`HotkeyRouting`).
- `installer/FolderDeck.iss`
  - 「Folder Deck 1회용」 줄에서 `HotKey` 와 그 주석을 지우고 새 주석을 달았다.
  - `[Registry]` — `ValueType: none` + `uninsdeletevalue dontcreatekey`. 설치는 Run 값을 만들지 않고(앱이 설정에
    맞춰 쓴다) 제거 때만 지운다. 앱이 남긴 값이 제거 뒤 죽은 경로를 가리키지 않게 하려는 것이다.
    - **이 방식을 택한 이유:** 값을 쓰는 주체가 앱 하나여야 「끔」 설정과 엇갈리지 않는다.
  - `[Run]` — 설치 끝에 `--agent`(`nowait postinstall skipifsilent` — 사용자가 끌 수 있다).
  - `[UninstallRun]` — 제거 전 `--agent-exit`(`runhidden waituntilterminated`).
  - `[Code] PrepareToInstall` — 업그레이드 전에 기존 `{app}\FolderDeck.exe --agent-exit` 를 부른다. 단
    **상주 뮤텍스(`Local\FolderDeck.Agent`)가 있을 때만**(`CheckForMutexes`) 부른다.
    - 처음엔 조건 없이 불렀다. 그런데 상주가 없는 옛 판(1.7.0 · 미리보기 설치본 — 판 번호가 같다)은 이 인자를
      몰라 「알 수 없는 인자」 선택 창을 띄우고 기다린다. 그러면 `ewWaitUntilTerminated` 가 업그레이드를 멈춘다.
    - 보고문을 쓰다 발견해 고쳤다. 옛 판은 이 뮤텍스를 만들지 않으니 이 검사가 그 경우를 거른다.
    - 뮤텍스 이름은 `.iss` 상수와 `InstanceNames.AgentMutex` 두 곳에 있다 — 바꿀 때 둘 다 고쳐야 한다.
  - **ISCC 컴파일**(Inno Setup 6, 출력은 scratchpad 로 돌림): **exit 0**. `artifacts\installer\` 는 안 건드렸다.
    설치·업그레이드·제거를 실제로 돌려 보지는 않았다(§7 손확인).

### A5 — 측정

**방식** — 실제 상주를 띄우면 실제 `%APPDATA%` 와 HKCU Run 키를 건드린다(§1 금지). 그래서 임시 루트 위에서 상주와
같은 길을 재는 **임시 측정 시험**을 만들어 Release 로 두 번 돌리고 지웠다.

- 쟀던 길: 숨은 창에 `WM_HOTKEY` 를 직접 보냄 → `GlobalHotkey` 콜백 → `ScratchWindowHost.Open()` → 실물
  `MainWindow` 의 `ContentRendered` 까지.
- **실제 키 입력 경로**(키보드 → 시스템이 `WM_HOTKEY` 를 넣는 단계)는 들어 있지 않다.
- 의존성은 실물이다(`ShellLauncher`·`RecycleBin` 등). 측정 중 창이 화면에 잠깐씩 떴다 닫혔다.

| 회 | 1차 실행 (ms) | 2차 실행 (ms) |
|---|---|---|
| 1번째 열기 (프로세스에서 처음) | 870 | 756 |
| 2번째 | 201 | 175 |
| 3번째 | 252 | 224 |
| 4번째 | 220 | 213 |
| 5번째 | 203 | 205 |

- **비교용(상주 없이 새 프로세스)**: 다시 재지 않았다. 앞 회차 실측(설치본 `--scratch` → 창 핸들까지)은
  670 · 654 · 1608 ms 였다. JH 가 체감한 「단축키로 느림」은 그 위에 탐색기의 .lnk 단축키 처리 시간이 더해진 것이다.
- **첫 번째가 이후보다 500 ms 넘게 느리다**(756~870 vs 175~252). 지시서대로 **데우기는 넣지 않았다.**
  - 제안: 상주가 시작할 때 1회용 창 하나를 화면 밖에서 만들었다 닫으면 첫 번째도 200 ms 대가 될 것으로 본다(재지 않음).
  - 대가는 상주 시작 직후 메모리가 조금 더 잡히는 것이다. JH 가 정한다.
- **메모리**(같은 측정 프로세스 — **xUnit 시험 호스트라 상주 단독의 값이 아니다**)

  | 시점 | 작업 집합(WS) | 관리 힙 |
  |---|---|---|
  | 5회 열고 닫은 뒤 | 403.6 / 402.6 MB | 24.2 / 24.2 MB |
  | 거기서 20회 더 열고 닫은 뒤(GC 후) | 455.8 / 447.0 MB | 16.6 / 21.8 MB |

  - 관리 힙은 늘지 않았다 → 닫힌 창의 관리 객체는 풀린다.
  - WS 는 20회에 약 45~52 MB 늘었다. 네이티브 쪽(WPF 렌더·셸 아이콘 캐시 등) 원인일 수 있으나 **가리지 않았다** — §6.
  - 실제 상주 프로세스 하나의 WS 는 재지 않았다.

---

## §4 A1 「앵커」 노출 자리 전수

`src/FolderDeck.App` 에서 화면 글자(XAML `Text`/`Header`/`ToolTip`/`AutomationProperties.Name`/`Content`,
C# 의 `Inform`/`Warn`/`Reject`/확인창 문자열)로 「앵커」가 나오는 자리를 grep 으로 뽑았다.

| 자리 | 1회용 창 | 이유 |
|---|---|---|
| `FolderPanelView.xaml` 「앵커 밖」 글자 | 감춤 | `ShowsAwayFromAnchor` |
| `FolderPanelView.xaml` ⌂ 「앵커로 복귀 (Alt+Home)」 버튼 · `Alt+Home` | 감춤 · 무동작 | `ShowsAnchorActions` · `GoToAnchorFromUserAsync` |
| `FolderPanelView.xaml` 「지금 보고 있는 폴더」 메뉴 「이 폴더를 앵커로 지정」 | 감춤 | `ShowsAnchorActions` |
| `ShellMenuPolicy.cs` 셸 메뉴 「이 폴더를 앵커로 지정」 | 넣지 않음 | `includeSetAnchor: false` |
| `MainWindow.xaml` 왼쪽 목록 안내 「재클릭 = 앵커로 복귀」 | 「재클릭 = 처음 폴더로」 | `FolderListHint` |
| `FolderPanelViewModel.LocationHint` 「앵커 밖: …」(왼쪽 목록 행에 보임) | 「지금 …」 | `HidesAnchor` 갈래 |
| `MainViewModel` 이름 바꾸기 확인창 「등록의 앵커 폴더 이름이 바뀌었다」 · 알림 「앵커도 따라갔다」 | 「등록 폴더의 이름이 바뀌었다」 · 「도 따라갔다」 | 1회용에서도 등록 폴더 이름을 바꾸면 닿는 자리 |
| `FolderPanelViewModel` `Reject("검색 타일에는 앵커로 지정할 폴더가 없다")` | 그대로 | 진입점(「앵커로 지정」 메뉴)이 1회용에서 감춰져 닿지 않음 |
| `MainViewModel` 「이미 앵커다」 · 「앵커를 옮겼다」 · 확인창 「앵커 지정」 | 그대로 | 같은 이유 — 셸 메뉴·현재 폴더 메뉴의 「앵커로 지정」에서만 닿음 |
| `MacroDialog.xaml` 툴팁 「앵커가 아니라 그때 타일에 떠 있는 경로가 대상이다」 | **그대로** | 매크로는 1회용 창에서도 열 수 있어 보인다. 매크로 대상이 「현재 경로」라는 뜻을 설명하는 문장이라 1회용에서도 틀리지 않다 — 거슬리면 다음 회차 |

---

## §5 지시서가 틀린 자리

1. **A3 「`ScratchShortcut` 등을 지운다」·A4 `AgentAutoStart`** — 회차 중 JH 결정(§2)으로 바뀌었다. 지시서
   잘못이라기보다 결정 변경이다.
2. **A3 「설정 창에 「지금 상주 시작」 버튼」** — 두지 않았다(§2). 저장이 띄우는 쪽이 한 단계 적고, 「버튼은 눌렀는데
   설정은 끔」 같은 엇갈림이 없다.
3. **A2 「1회용 창 열기를 맡는 코드를 App 밖 클래스로 빼서 STA 시험」** — 그렇게 했다(`ScratchWindowHost`). 다만
   트레이·뮤텍스·설정 감시(`AgentRuntime`)는 `Application` 인스턴스가 필요해 자동 시험이 없다. 그 부분은 손확인 몫이다.

---

## §6 범위 밖 발견 · 확인 못 한 것

- **상주 안 1회용 창에서 「상주로 받기」를 끄면 그 창이 닫힌다.** 상주가 설정 감시로 「끔」을 받고 `Exit()` 하면서
  자기 안의 1회용 창을 모두 닫는다 — 지금 설정을 바꾼 바로 그 창도 포함이다. 저장할 것이 없어 잃는 것은 없지만
  놀랄 수 있다. 막으려면 상주를 「창이 다 닫힐 때까지 단축키만 풀고 기다렸다 끝나기」로 바꾸면 된다. JH 판단.
- **개발 빌드를 실제 사용자 계정으로 띄우면 Run 키가 개발 빌드 exe 를 가리키게 된다.** 모든 시작 경로가 라우팅을
  맞추고, Run 값은 `Environment.ProcessPath` 다. 설치본을 다시 한 번 띄우면 되돌아온다. 이번 회차는 개발 빌드를
  실제 계정으로 띄우지 않았다.
- **업그레이드 때 옛 exe 에 `--agent-exit` 를 부르는 문제 — 고쳤다(§3 A4).** 상주 뮤텍스가 있을 때만 부른다.
  실제 업그레이드로 확인하지는 않았다(손확인 §7-7).
- WS 증가(20회 열고 닫기에 약 45~52 MB, 관리 힙은 그대로) — 원인 미확인. 상주는 오래 사는 프로세스라 실제 상주로
  하루 쓰고 작업 관리자에서 보는 것이 가장 싸다.
- 상주가 설정 파일을 손상 상태로 읽으면(다른 프로세스가 쓰는 중) 그 알림을 버린다. 다음 쓰기가 다시 알리지만,
  그 쓰기가 끝까지 없으면 상주는 이전 값으로 남는다(재지 않음).

---

## §6-2 보고 뒤 추가 — JH 결정 둘 반영 + 패키징 (같은 날)

보고 뒤 JH: 「둘 다 넣고 패키징해줘」. 「둘」은 이 보고문의 열린 결정 둘이다 — A5 의 데우기, §6 첫 항목의
「창이 다 닫힐 때까지 기다렸다 끝나기」.

- **데우기** — `ScratchWindowHost.WarmUp()`. 상주가 서면 한가할 때(`ApplicationIdle`) 1회용 창 하나를 만든다.
  화면 밖(-32000), 작업 표시줄 숨김, 포커스 안 뺏기로 띄우고, 첫 렌더 뒤 닫는다. 이 창은 `OpenWindows` 에 안
  들어가고 「마지막 창 닫힘」도 쏘지 않는다.
  - **재측정**(A5 와 같은 임시 측정 시험, Release 두 번, 측정 뒤 삭제):

    | | 1차 실행 | 2차 실행 |
    |---|---|---|
    | 데우기 자체(뒤에서, 상주 시작 직후) | 1419 ms | 1208 ms |
    | 데우기 뒤 1번째 열기 → `ContentRendered` | 226 ms | 207 ms |
    | 2번째 | 266 ms | 263 ms |
    | 3번째 | 269 ms | 250 ms |

    첫 번째가 데우기 전 756~870 ms 에서 207~226 ms 로 내려왔다.
- **창이 다 닫힐 때까지 기다렸다 끝나기** — `AgentRuntime.Retire()`. 설정 감시로 「상주로 받기 끔」을 받으면 바로
  `Exit()` 하지 않고, 단축키·트레이·신호 수신·설정 감시·**뮤텍스**를 곧바로 내려놓는다. 그다음:
  - 열린 1회용 창이 없으면 바로 끝난다.
  - 있으면 `ScratchWindowHost.LastWindowClosed` 를 기다렸다 끝난다.
  - 뮤텍스를 먼저 놓으므로 그사이 다시 켜면 새 상주가 뜰 수 있다.
  - 그사이 시작 메뉴 바로가기는 물러난 상주에게 맡기지 않는다 — 신호 수신을 내려놓아 `IsRunning` 이 거짓이 된다.
  - `Retire()` 자체는 `Application` 이 필요해 자동 시험이 없다. 그것이 기대는 호스트 동작 둘에 시험을 더했다.
- 새 시험 둘(`ScratchModeTests`)
  - `TheWarmUpWindowClosesItselfUncounted`
  - `LastWindowClosedFiresOnlyWhenTheLastOneCloses`
- 시험: Core 408 · App **1009**, 두 번 연속 전부 통과.
- **패키징**
  - `dotnet publish … -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none
    -o artifacts/publish/win-x64`(publish 폴더는 비우고 다시 만듦)
  - ISCC → `artifacts\installer\FolderDeck-Setup-1.7.0-preview2.exe`(53,598,558 바이트). 판 번호는 안 올렸다.
    기존 `FolderDeck-Setup-1.7.0.exe`·`FolderDeck-Setup.exe`·`-preview.exe` 는 덮지 않았다.
  - ISCC 첫 시도는 `EndUpdateResource failed (110)` 로 실패했다. ISCC 자신이 백신의 출력 폴더 간섭을 짚는 메시지다.
    스크립트는 바꾸지 않고 다시 돌려 첫 재시도에 exit 0.
  - 크기가 앞 미리보기(46.6 MB)보다 약 7 MB 큰 것은 `UseWindowsForms` 로 WinForms 런타임이 함께 실려서다.
    publish 폴더에 `System.Windows.Forms.dll` 있음 확인.

## §6-3 JH 손확인 1차 결과 — 버그 둘 수정 + 재패키징 (같은 날)

JH 손확인(preview2): 「나머지는 잘됨, 재부팅은 나중에」. 버그 둘을 보고받았다.

1. **「상주 프로그램으로 받기」를 끄면 상주는 꺼지는데 단축키로 실행이 안 된다.**
   - **원인(추정 — 증상과 순서가 맞는다, 직접 재지는 못함):** 끄는 저장이 바로가기(.lnk)에 키를 **먼저** 썼다.
     탐색기는 바로가기 키가 바뀔 때 그 키를 시스템에 등록하는데, 그 순간 상주가 아직 같은 키를 `RegisterHotKey` 로
     쥐고 있어 실패한다. 상주는 0.25초 뒤(설정 감시)에야 키를 놓고, 탐색기는 다시 시도하지 않는다.
     - 앞 회차(상주 없음)에서 F → G 바꾸기가 동작했던 것과도 맞는다.
   - **고침:**
     - `IHotkeyRouting.Apply(settings, agentHoldsKey)` — 「끔」인데 상주가 떠 있으면(`Agent.IsRunning`) 바로가기 키를
       아직 쓰지 않는다(Run 키는 지운다).
     - 상주가 `Retire()` 에서 **단축키를 먼저 놓고**(`_binder.Clear()`) 그다음 `Apply(settings)` 로 바로가기에 키를 쓴다.
     - 반대 방향(켤 때)은 탐색기가 막 놓은 키를 상주가 곧바로 못 잡을 수 있다. 그래서 등록 실패 시 0.5초 간격으로
       6번(3초) 다시 시도한 뒤에야 트레이 알림을 낸다.
   - **시험:**
     - `WhileTheAgentStillHoldsTheKeyTheShortcutStaysEmpty` — 키를 쥔 동안 바로가기는 「없음」, 놓은 뒤 다시 부르면 키를 씀.
     - `TurningTheAgentOffRoutesToTheShortcutWithoutStartingAnything` 에 「`agentHoldsKey` = 참으로 넘김」 단언을 더함.
     - 「탐색기가 실제로 다시 등록하는가」는 자동 시험이 닿지 않는다 — 손확인 7번.
2. **트레이 「상주 끝내기」 뒤 앱을 다시 실행해도 트레이에 안 들어간다**(설정에서 껐다 켜야 들어감).
   - **원인(확정):** 상주를 띄우는 곳이 설치 직후 · 로그인(Run 키) · 설정 저장 셋뿐이었다.
   - **고침:** `App.EnsureAgent()` — 「상주로 받기」가 켜져 있고 설정을 쓸 수 있는 실행인데 상주가 없으면, **모든 시작
     경로**(선택 창 · 작업 관리 · `--scratch`)에서 상주를 새 프로세스로 띄운다. 판정은 순수 함수
     `HotkeyRoutingRules.ShouldStartAgent`(시험 넷).
     - `--scratch` 는 이번 창을 자기 프로세스에서 열고 상주를 같이 띄운다.
     - `--agent`·`--agent-exit` 경로는 부르지 않는다.
- 시험: Core 408 · App **1014**(+5), 두 번 연속 전부 통과.
- **재패키징:** `artifacts\installer\FolderDeck-Setup-1.7.0-preview3.exe`(53,605,917 바이트) — publish 폴더를 비우고
  다시 만들고 ISCC 첫 시도 exit 0. 판 번호 그대로, 앞 미리보기들은 덮지 않았다.

## §6-4 JH 손확인 2차 결과 — 「상주 끝내기」 뒤 단축키 넘겨주기 (같은 날)

JH 손확인(preview3): **§6-3 의 두 버그 포함 「다 잘됨」**(재부팅은 나중에).

이어서 JH 가 물었다: 「상주로 받기 켜 두고 트레이에서 상주를 끈 뒤 단축키를 누르면 떠야 하지 않나?」
- 당시 동작은 의도된 것이었다. 「켬」이면 창이 두 번 뜨지 않게 바로가기 키를 지워 두므로, 상주를 끝내면 받는 곳이 없다.
- 코웍이 「끝날 때 바로가기에 넘겨주기」를 제안했고, JH 가 「그렇게 바꾸고 커밋해줘」로 확정했다.

- **`IHotkeyRouting.HandOverToShortcut(settings)`** — 바로가기에 설정의 단축키를 쓴다. Run 키는 건드리지 않는다 —
  설정은 여전히 「켬」이다.
- **`AgentRuntime.Exit()`** — 「끔」으로 물러난 경우(`Retire`, 이미 맞춤)가 아니면, 즉 트레이 「상주 끝내기」와
  `--agent-exit` 일 때 넘겨준다. 순서는 **단축키를 먼저 놓고**(재시도 타이머 정지 · `_binder.Clear()`) → 넘기기 →
  창 닫기 · 정리 · 종료다. §6-3 과 같은 이유다(탐색기 등록이 상주에 막히지 않게).
- **이어지는 흐름:**
  1. 상주가 없는 동안 단축키 → 탐색기가 `--scratch` 를 띄운다(느림).
  2. 그 프로세스가 시작할 때 `HotkeyRouting.Apply` 로 바로가기 키를 다시 지우고, `EnsureAgent` 로 상주를 띄운다.
  3. 새 상주는 등록 재시도(최대 3초)로 키를 잡는다.

  **끝낸 뒤 첫 누름만 느리고 그다음부터는 상주가 빠르게 받는다.** 이 연쇄 전체는 자동 시험이 닿지 않는다 — 손확인 몫.
- 업그레이드·제거 때 `--agent-exit` 도 넘겨준다. 업그레이드 뒤 새 상주가 다시 지우고, 제거는 바로가기 자체를 지운다 —
  남는 것이 없다.
- 시험: `HandingOverWritesTheShortcutKeyAndKeepsTheRunKey`(넘기면 키 · Run 키 그대로 · 다음 `Apply` 가 다시 지움).
  Core 408 · App **1015**, 두 번 연속 전부 통과.
- **이 변경은 아직 패키징·손확인하지 않았다.** preview3 에는 들어 있지 않다.

---

## §7 JH 손확인 목록

**새 설치본으로**(이 빌드를 publish → ISCC).

1. 설치 → 트레이에 FolderDeck 아이콘이 있는가. `Ctrl+Alt+F` 로 1회용 창이 **바로** 뜨는가(첫 번째는 조금 늦을 수 있다 — A5).
2. 연달아 다섯 번 눌러 창 다섯이 뜨는가. 다 닫아도 트레이 아이콘이 남는가. 트레이 더블클릭도 창을 여는가.
3. 설정 → 「1회용 창」 → 단축키 `Ctrl+Alt+G` → 저장 → 곧 새 키로 뜨고 옛 키는 안 먹는가.
4. 다른 프로그램이 쓰는 키로 바꾸면 트레이 알림이 뜨는가.
5. 「상주 프로그램으로 받기」를 끄고 저장 → 트레이 아이콘이 사라지고, 단축키는 (느리게라도) 바로가기로 뜨는가.
   **설정을 바꾼 그 1회용 창(상주 안 창)이 닫히지 않고 남는가**, 그 창을 닫으면 상주 프로세스가 작업 관리자에서
   사라지는가. 다시 켜면 상주가 뜨는가. 끈 상태로 로그아웃·로그인하면 상주가 없는가.
6. 재부팅(또는 로그아웃·로그인) 뒤 단축키가 살아 있는가.
7. **업그레이드 설치** 둘 — (a) 미리보기 설치본 위에 이번 설치본: 멈추거나 선택 창이 뜨지 않고 끝나는가.
   (b) 이번 설치본의 상주가 떠 있는 상태에서 한 번 더 설치: 상주가 끝나고 덮어쓴 뒤 다시 뜨는가. 제거 뒤 트레이
   아이콘과 자동 시작이 남지 않는가.
8. 트레이 「상주 끝내기」 뒤 단축키가 안 먹고, 시작 메뉴 「Folder Deck 1회용」은 여전히 뜨는가.
9. 1회용 창: 하위 폴더로 들어가도 「앵커 밖」이 안 뜨고 경로가 드라이브부터 보이는가. 폴더가 떠 있는 칸을 클릭하고
   즐겨찾기를 누르면 그 칸이 그 폴더로 바뀌는가. ⌂ 버튼과 「앵커로 지정」 메뉴가 안 보이는가.
10. 일반 작업 관리 창은 앵커 표시·메뉴·브레드크럼이 예전 그대로인가.
11. (앞 회차 이월) 빈 칸을 클릭하면 파란 선이 그 칸으로 옮겨 가는가.
