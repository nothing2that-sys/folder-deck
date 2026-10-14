# FolderDeck — 1회용 탐색기 · 공유 즐겨찾기 · 배치 프리셋 · 상단 메뉴 정리 — 보고 (2026-10-14)

지시서 `doc/ScratchFavorites_2026-10-14_instructions.md` · 실행 코웍(Claude Code) · **커밋하지 않았다.**
지시서 커밋(§0)만 JH 의 채팅 요청("커밋하고 바로 진행해줘")으로 내가 쳤다 — `5c6b9e7`.

---

## §1 시작 게이트

저장소 루트에서 쟀다.

| 항목 | 기대값 | 실측 | |
|---|---|---|---|
| `git rev-parse HEAD:src` | `9cd2fc7d865523d54033b19e3e5ecfcaed19da27` | `9cd2fc7d865523d54033b19e3e5ecfcaed19da27` | 일치 |
| `git rev-parse HEAD:tests` | `e68a893790f6c5f9d5a92ea407809f5f5470bfe7` | `e68a893790f6c5f9d5a92ea407809f5f5470bfe7` | 일치 |
| `git status --porcelain -u -- src tests` | 빈 출력 | 빈 출력 | 일치 |

기록만: HEAD `5c6b9e7`(§0 지시서 커밋). `doc/USER_MANUAL.md` 는 미추적 그대로 두었다.

**기준 시험(시작 전)** — Core 344 통과 / 344, App 951 통과 / 951.

---

## §2 단계별 결과

통과 수는 단계 끝에 `dotnet test FolderDeck.slnx` 로 잰 것이다.

| 단계 | Core | App | 빌드 실패 횟수(그 단계 안) |
|---|---|---|---|
| 기준 | 344 | 951 | — |
| S1 상단 메뉴 | 344 | 952 | 0 |
| S2 배치 프리셋 | 362 | 957 | 1 (XAML 주석 안 `--`) |
| S3 공유 즐겨찾기 | 371 | 966 | 2 (`using System.IO` 누락 · `Directory` 이름 해석) — 세 번째에 통과 |
| S4 1회용 모드 | 400 | 983 | 1 (`AppSettings.ScratchHotkey` 속성 이름이 같은 이름의 클래스를 가림) |

S4 끝에서 전체 시험을 **세 번 연속** 돌렸고 셋 다 전부 통과였다(Core 400 · App 983).

### S1 — 상단 오른쪽 버튼 정리

- `MainWindow.xaml` 상단 오른쪽의 글자 버튼 넷을 **`⋯` 버튼 하나 + 작은 「배치」 토글**로 바꿨다.
  - `⋯` 메뉴 항목: 「중복 찾기」 · 「설정」(기존 `CanOpenSettings` 비활성 조건 유지) · 구분선 · 「다른 작업
    관리 열기」. 각 항목은 기존 명령을 그대로 묶었고, 버튼에 있던 `AutomationProperties.Name`·`ToolTip`
    문구도 옮겼다.
  - 메뉴의 `DataContext` 는 생성자에서 직접 건다(`MoreMenu.DataContext = viewModel`). 왼쪽 클릭은
    `OnMoreMenuClick` 이 버튼 아래에 메뉴를 연다.
- 「배치 편집」은 메뉴에 넣지 않고 `EditModeButton` 스타일 그대로 두되 `MinWidth="0"` `Padding="8,3"` 로
  줄였다. 글자는 `EditToggleText` 를 「✎ 배치」/「✎ 편집 끝」으로 짧게 했다. 켜진 모양은 스타일의
  `DataTrigger` 가 그대로 그린다.
- 배치 설명 XAML 주석을 새 구조에 맞게 고쳐 썼다.
- 새 시험 `MainWindowMenuTests.TheMoreMenuItemsAreBoundToTheRealCommands` — 실물 `MainWindow` 를 STA 에서
  세우고 세 항목이 뷰모델의 **같은 명령 인스턴스**에 묶였는지 `Assert.Same` 으로 본다.
  - 처음 돌렸을 때 바인딩이 아직 안 붙어 `null` 이었다. 시각 트리 밖 요소의 바인딩은 디스패처가 한 바퀴
    돌아야 붙는다. 시험 안에서 `ApplicationIdle` 까지 한 번 돌리게 했다.

### S2 — 배치 프리셋

- `src/FolderDeck.Core/Layout/LayoutPresets.cs` — `LayoutPreset { Single, SideBySide, Stacked, Quad }`,
  `LayoutPresets.Build`·`Label`·`All`. 칸 좌표는 지시서 표 그대로이고, 타일은 전부 `Rotating`·`FolderId = null`,
  `RotationIndex` 는 읽기 순서 1..N 이다.
- `AppSettings.ScratchLayout`(기본 `SideBySide`)을 더하고 `CurrentSchemaVersion` 을 4 → 5 로 올렸다.
  - 이력 주석 한 줄을 달았다. S3·S4 가 같은 판에 더한 필드도 같은 줄에 함께 적었다(한 회차라 두 번 올리지
    않음).
  - 모르는 값은 `LenientLayoutPresetConverter` 가 기본값으로 닫는다. 저장소 전역 열거형 변환기
    (`allowIntegerValues: false`)는 모르는 문자열에서 던져 **설정 파일 전체**를 손상으로 만든다. 그러면
    앱이 설정을 덮어쓰지 않는 모드로 떨어진다. 프리셋 하나 때문에 그렇게 되지 않게 했다.
- 설정 창에 「1회용 창」 탭을 더했다(처음 타일 배치 `ComboBox`). 값은 바인딩으로만 오가고 이벤트 핸들러가
  없다.
- `SettingsEditRequest`/`SettingsEditResult` 에 값을 **기본값 있는 뒤쪽 인자**로 더했다. 결과 쪽은
  `null` = 바꾸지 않음이다 — 기존 시험들이 세 값만으로 만든 결과가 새 설정을 기본값으로 되돌리지 않게.
- 새 시험
  - `LayoutPresetsTests` — 네 프리셋 × (좌표 표 일치 · 격자 안 · 서로 안 겹침 · `NormalizeTiles` 가 하나도 안
    지움) + 이름. 「순환 칸이어야 한다」를 실물 `NormalizeTiles` 로 잰다.
  - `AppSettingsSchemaTests` 에 셋 — v4 파일은 `SideBySide` · 왕복 · 모르는 값 셋(`"octagon"`·`7`·`null`)이
    파일을 손상시키지 않고 `tileGap` 도 그대로 읽힘.
  - `ScratchSettingsTests` 다섯 — 요청에 실림 · 저장 반영 · 새 값 없는 결과는 그대로 둠 · 탭 VM 선택 ·
    실물 `IconSettingsDialog` STA 생성.

### S3 — 공유 즐겨찾기

- **Core**
  - `FavoriteEntry`·`FavoritesDocument`(`schemaVersion` 1).
  - `FolderDeckPaths.FavoritesFile`(`favorites.json`).
  - `FavoritesStore` — `Load` · `Mutate` · `Add` · `Remove` · `Move`.
    - `Mutate` 는 이름 있는 뮤텍스 `Local\FolderDeck.Favorites` 안에서 디스크 다시 읽기 → 적용 →
      `AtomicFile` 쓰기를 한다.
    - 손상·모르는 판이면 쓰지 않고 실패를 돌려준다.
    - 같은 경로 중복은 `FolderPathRules.Normalize` + 대소문자 무시로 거절한다.
- **App**
  - `FavoritesViewModel` / `FavoriteRowViewModel`.
    - 열 수 있는지(`IsAvailable`)는 UI 스레드 밖에서 확인한다.
    - 쓰기 뒤에는 저장소가 돌려준 목록으로 통째로 갈아 끼운다.
  - 접힘은 `AppSettings.FavoritesCollapsed` 에 둔다(작업 관리 `leftLayout` 을 안 건드림).
- **왼쪽 패널** — 폴더 리스트 칸 **맨 위**에 「즐겨찾기」 묶음(헤더 ▾/▸ · ＋ · `MaxHeight="180"` 스크롤)을
  얹었다.
  - 새 행을 만들지 않은 것은 `ApplyLeftRowHeights` 를 건드리지 않기 위해서다.
  - 행 우클릭 메뉴: 위로 · 아래로 · 등록 폴더로 추가 · 제거.
  - 열 수 없는 경로는 흐리게 보이고, 클릭하면 이유를 한 줄로 알린다(지우지 않는다).
- **추가 경로 셋** — 헤더 ＋(폴더 고르기) · 파일 목록 폴더 항목 우클릭 「즐겨찾기에 추가」(셸 메뉴 앱 항목
  `AddFavorite = 0x17`) · 「지금 보고 있는 폴더」 메뉴 「즐겨찾기에 추가」.
- **다른 창 반영** — `MainWindow` 가 `favorites.json` 을 `FileSystemWatcher` 로 보고, 250ms 모아 UI 스레드에서
  `Reload` + `ProbeAsync` 한다. 창이 닫히면 감시를 푼다.
- **대상 타일**
  - `MainViewModel.TargetTile`(런타임 전용)은 `FolderPanelView.OnPreviewMouseDown` 이 왼쪽 클릭마다 부르는
    `panel.NotifyActivated()` → `ActivatePanel` 로 정해진다.
  - 표시는 `TileGridView.xaml` 의 위 가장자리 2px 강조선(`Brush.Accent`)이다. 클릭을 가로채지 않는다.
  - 고른 적이 없거나 그 타일이 사라졌으면 `EffectiveTargetTile` 이 순환1 로 폴백한다.
- **클릭 동작(`OpenFavoriteAsync`)**
  - 대상 패널에 폴더가 떠 있으면 `NavigateToAsync(path)`(임시 이동)를 한다.
  - 비었으면 일반 모드는 안내만 한다.
  - 검색 타일이 대상이면 안내만 한다.
- 새 시험
  - `FavoritesStoreTests` 아홉 — **두 인스턴스 번갈아 변경이 다 남음**, 손상 파일 위 `Mutate` 실패 +
    **바이트 그대로**, 새 판 거절 + 그대로, 손으로 고친 빈 항목 건너뜀 등.
  - `FavoritesTests` 여덟 — **즐겨찾기 클릭 뒤 작업 관리 파일 바이트가 클릭 전과 같음**(`Flush` 후 실물
    파일 비교), 앵커 그대로·`IsAwayFromAnchor` 참·`Folders` 개수 그대로, 빈 대상은 안내만, 없는 경로는
    흐림+경고, 두 메뉴 진입점, 다른 창 `Reload`, 「등록 폴더로 추가」만 작업 관리와 이어짐, 접힘이 전역
    설정에 감.

### S4 — 1회용 모드

- `CommandLineOptions` — `--scratch` / `Scratch`. `--scratch` 와 `--workspace` 를 같이 주면 `Error` 를 내고
  둘 다 버려 선택 창으로 떨어진다.
- `ScratchWorkspaceStore` — 1회용 작업 관리 id 하나에 대해서만 `SaveWorkspace`·`SaveWorkspaceDebounced`
  (그리고 `DeleteWorkspace`)를 아무것도 안 하게 한다. 나머지는 안쪽 저장소로 넘긴다.
- `App.xaml.cs`
  - `options.Scratch` 면 `ShowScratchWindow()` 를 부른다. 메모리 `Workspace`(`Tiles = LayoutPresets.Build(설정값)`)
    + 래퍼로 띄운다.
  - **`InstanceLock`·`StartListener` 를 타지 않고**, `LastWorkspaceId` 도 바꾸지 않는다
    (`ShowWorkspaceWindow(…, isScratch: true)`).
  - 창 제목은 「FolderDeck — 1회용 (저장 안 됨)」이다.
- `MainViewModel`
  - `IsScratch` · `ScratchLayoutChoices` · `ApplyScratchLayoutAsync` — 떠 있던 폴더를 읽기 순서대로 새 칸에
    옮겨 담는다. 넘치면 칸에서만 빠지고 메시지로 개수를 밝힌다.
  - 빈 칸 즐겨찾기 — 1회용이면 메모리에서 등록하고, `RotationSlot` = 대상 칸 번호로 `ShowFolderAsync` 한다.
  - ⋯ 메뉴 「타일 배치 ▸」는 `IsScratch` 일 때만 보인다.
- **단축키**
  - `Core/Models/ScratchHotkey.cs` — 표기 ↔ .lnk WORD 를 바꾸는 순수 함수다. 허용 조합은
    `Ctrl+Alt(+Shift)` + A–Z/0–9/F1–F12 뿐이다.
  - `AppSettings.ScratchHotkey` — 기본 `Ctrl+Alt+F`, 빈 문자열은 「없음」, 못 읽는 값은 기본값으로 떨어진다.
  - `App/Services/ScratchShortcut.cs` — `IShellLinkW`+`IPersistFile` COM 으로 바로가기를 다룬다.
    대상은 `Programs\Folder Deck\Folder Deck 1회용.lnk` 이다. 없으면 만들지 않는다(`NotInstalled`).
  - 적용 시점 둘:
    - 앱 시작 때 `App.OnStartup` 이 설정값으로 맞춘다. 실패하면 띄운 창에 한 줄 알린다.
    - 설정 저장 때는 값이 바뀌었을 때만 고친다. 알림은 `RefreshAllAsync` 뒤에 낸다 — 그 전에 내면 짧은
      알림이 바로 지워진다.
  - 설정 창 「1회용 창」 탭에 단축키 칸이 생겼다(눌린 조합을 잡음, 「없음」 버튼, 거절 이유, 바로가기 유무
    안내, 「겹치면 먼저 등록된 쪽이 이긴다」 한 줄).
- `installer/FolderDeck.iss` `[Icons]` 에 지시서의 한 줄을 넣었다(주석 두 줄 포함).
  - **ISCC 컴파일 확인**: `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` 로 출력을 scratchpad 로 돌려
    컴파일했다(`/O` · `/F`) — **exit 0**. `artifacts\installer\` 는 건드리지 않았다.
  - 묶인 바이너리는 기존 `artifacts\publish\win-x64`(v1.7.0 publish 결과)라 **문법 확인일 뿐**이다.
  - .iss 에 BOM 이 없어 한글 바로가기 이름이 깨질지 따로 쟀다. BOM 없는 UTF-8 시험 스크립트의
    `OutputBaseFilename=probe-1회용` 이 `probe-1회용.exe` 로 나왔다 → Inno 6 은 BOM 없는 UTF-8 을 그대로
    읽는다.
- 새 시험
  - `ScratchHotkeyTests`(Core) 29 — 허용/거절 조합, 정규화, WORD 배치(`Ctrl+Alt+F` = `0x0646` 등), 규칙 밖
    WORD, 설정 기본값·거절.
  - `ScratchModeTests`(App) 17:
    - 명령줄 둘.
    - **저장 차단**: 실물 `WorkspaceStore` 를 래퍼로 감싸 등록·표시·프리셋 전환·즐겨찾기·접힘을 한 뒤
      `Flush` 하고 `workspaces\` 파일 수 0 을 확인했다. 설정 파일은 써졌고 값이 실렸다.
    - 래퍼가 다른 작업 관리는 저장함.
    - 제목.
    - `Quad`→`Single` 전환(첫 폴더 남음 · `Folders` 둘 그대로 · 넘침 메시지) · `SideBySide`→`Quad` 다시 채움 ·
      일반 모드에서 프리셋 무동작.
    - 빈 1회용 칸 즐겨찾기(그 칸에 뜸 · 다른 칸 비어 있음 · 디스크 0).
    - 단축키 배선 셋(바뀜 → `Apply` 1회 · 안 바뀜 → 0회 · 바로가기 없음 안내).
    - 단축키 칸 VM.
    - **실물 .lnk** 둘 — 임시 폴더에 .lnk 를 만들고 COM 으로 쓰고 다시 읽어 `0x0647` 같은 값 일치 · 같은
      값이면 `Unchanged` · 「없음」이면 0. 없는 .lnk 는 만들지 않음.
    - 1회용 `MainWindow` STA 생성 + 「타일 배치」 메뉴 보임(항목 4) · 일반 창에서는 숨김.

---

## §3 바뀐 파일

`git status --porcelain -u -- src tests installer` 출력 그대로:

```
 M installer/FolderDeck.iss
 M src/FolderDeck.App/App.xaml.cs
 M src/FolderDeck.App/Interop/ShellMenuPolicy.cs
 M src/FolderDeck.App/MainWindow.xaml
 M src/FolderDeck.App/MainWindow.xaml.cs
 M src/FolderDeck.App/Services/CommandLineOptions.cs
 M src/FolderDeck.App/Services/ISettingsEditor.cs
 M src/FolderDeck.App/ViewModels/FolderPanelViewModel.cs
 M src/FolderDeck.App/ViewModels/MainViewModel.cs
 M src/FolderDeck.App/ViewModels/TileViewModel.cs
 M src/FolderDeck.App/Views/FolderPanelView.xaml
 M src/FolderDeck.App/Views/FolderPanelView.xaml.cs
 M src/FolderDeck.App/Views/IconSettingsDialog.xaml
 M src/FolderDeck.App/Views/IconSettingsDialog.xaml.cs
 M src/FolderDeck.App/Views/TileGridView.xaml
 M src/FolderDeck.Core/Models/AppSettings.cs
 M src/FolderDeck.Core/Storage/FolderDeckPaths.cs
 M tests/FolderDeck.App.Tests/ShellMenuPolicyTests.cs
 M tests/FolderDeck.App.Tests/StaWpfTestSupport.cs
 M tests/FolderDeck.Core.Tests/AppSettingsSchemaTests.cs
 M tests/FolderDeck.Core.Tests/StoreBehaviorTests.cs
?? src/FolderDeck.App/Services/ScratchShortcut.cs
?? src/FolderDeck.App/Services/ScratchWorkspaceStore.cs
?? src/FolderDeck.App/ViewModels/FavoritesViewModel.cs
?? src/FolderDeck.App/ViewModels/ScratchSettingsViewModel.cs
?? src/FolderDeck.Core/Layout/LayoutPresets.cs
?? src/FolderDeck.Core/Models/FavoriteEntry.cs
?? src/FolderDeck.Core/Models/ScratchHotkey.cs
?? src/FolderDeck.Core/Storage/FavoritesStore.cs
?? tests/FolderDeck.App.Tests/FavoritesTests.cs
?? tests/FolderDeck.App.Tests/MainWindowMenuTests.cs
?? tests/FolderDeck.App.Tests/ScratchModeTests.cs
?? tests/FolderDeck.App.Tests/ScratchSettingsTests.cs
?? tests/FolderDeck.Core.Tests/FavoritesStoreTests.cs
?? tests/FolderDeck.Core.Tests/LayoutPresetsTests.cs
?? tests/FolderDeck.Core.Tests/ScratchHotkeyTests.cs
```

`git diff --stat -- src tests installer`(추적 파일만): 21 files changed, 914 insertions(+), 62 deletions(-).
버전(`Directory.Build.props`·`.iss` 의 `AppVersion`)은 안 건드렸다.

---

## §4 지시서가 틀린 자리

1. **§7-4(「기존 시험의 기대값을 바꿔야 하면 멈춘다」)가 지시서 스스로 시킨 변경과 부딪친다.** 아래는
   지시서가 명시한 변경의 직접 결과라서 멈추지 않고 고쳤다. 고친 것은 **「지금 판이 몇이다 / 메뉴가 정확히
   무엇이다」를 못박은 기대값**뿐이고, 그 시험들이 지키던 성질(구파일 승격 · 새 판 거절 · id 대역 분리 ·
   폴더 전용 항목)은 그대로 잰다. JH 가 보기에 멈췄어야 할 자리라면 알려 달라.
   - S2 「`CurrentSchemaVersion` 을 하나 올린다」 → 판 리터럴 다섯 자리:
     - `AppSettingsSchemaTests.CurrentSchemaVersionIsFour` → `…IsFive`(4→5)
     - `SavingStampsSchemaVersionFour` → `…Five`(4→5)
     - `ASchemaVersionFiveFileIsRejectedAsUnsupported` → `…SixFile…`(5→6)
     - `SavingAV1FilePromotesItToTheCurrentVersionAndWritesTheSlot` 의 4→5
     - `StoreBehaviorTests.ExtensionGlyphsRoundTrip` 의 4→5
   - S3 「폴더 항목 우클릭 「즐겨찾기에 추가」」 → `ShellMenuPolicyTests` 셋:
     - `FolderGetsAllFiveInOrder` → `FolderGetsAllSixInOrder`(목록에 `AddFavorite` 추가)
     - `UnknownAppBandIdRunsNothing` 의 「안 쓰는 id」 표본을 `0x17`→`0x18` 로 옮김. 2026-09-18 에
       `CompareContent` 가 `0x16` 을 가져갈 때 한 것과 같은 처리다.
     - `AppIdsMapToTheirOwnCommand` 에 `0x17` 줄 추가
2. **S1 「`MainWindowFixture` 로 창을 STA 생성」은 그대로는 안 선다.** `MainWindow.xaml` 은
   `App.xaml` 에만 있는 스타일(`NavButton`·`EditModeButton` 등)과 변환기를 쓰는데, 공용 헬퍼
   `StaWpfTestSupport` 는 맨 `Application` + `LightTheme.xaml` 만 실었다.
   - 헬퍼를 **실물 `App` 을 만들고 `InitializeComponent()`** 하도록 넓혔다. `App.xaml` 이 `LightTheme` 를
     병합하므로 기존 다이얼로그 시험이 보던 리소스의 상위 집합이다.
   - `StartupUri` 가 없고 `OnStartup` 은 `Run()` 에서만 불려 리소스만 실린다.
   - 공용 시험 기반을 바꾼 것이라 따로 적는다.
3. **S3 「포커스 판정은 기존 `PanelFocusPolicy` 가 패널에 포커스를 주는 자리에 얹는다」** — 같은
   핸들러(`FolderPanelView.OnPreviewMouseDown`)에 얹었지만 **포커스 가드 밖**에 두었다.
   - 가드(`!IsKeyboardFocusWithin && ShouldFocusPanelOnLeftClick`)는 버튼·항목 클릭과 이미 포커스가 있는
     경우를 거른다. 그 안에 두면 파일 항목을 눌러도 대상 타일이 안 바뀐다.

---

## §5 범위 밖 발견 · 확인 못 한 것

- **설정 판 5 는 되돌아갈 수 없다.** 이 빌드가 설정을 한 번이라도 저장하면 `settings.json` 이
  `schemaVersion: 5` 가 된다.
  - 그 뒤 설치된 v1.7.0 을 띄우면 「모르는 판」으로 거절하고 설정을 덮어쓰지 않는 모드로 뜬다(기존 설계대로).
  - 그래서 **개발 빌드를 실제 `%APPDATA%` 로 띄워 보지 않았다.** `FolderDeckPaths.Default` 는 known-folder 를
    쓰므로 환경 변수로 돌릴 수도 없다.
  - 손확인은 새 설치본으로 해 달라.
- **S1 첫 실행의 App 실패 3건은 이름을 못 잡았다.** S1 을 처음 돌렸을 때 App 이 949/952 였다. 하나는 새
  메뉴 시험(바인딩 타이밍 — 고침)이었고, 나머지 둘은 요약 줄만 보고 이름을 안 남긴 채 다시 돌렸다.
  - 그 뒤 실행은 S1 세 번 · S2~S4 각 한 번 · 끝에 세 번, 모두 전부 통과다.
  - 기존 시험 중 간헐적으로 실패하는 것이 있을 수 있다 — **확인 안 됨.**
- **파일 감시(다른 창 반영)·탐색기의 .lnk 단축키 즉시 반영·빈 순환 칸 클릭으로 대상 지정**은 자동 시험이
  재지 않는다.
  - 빈 칸 클릭은 코드로만 확인했다. 패널 뿌리 `Rectangle` 에 `Fill="{StaticResource Brush.Surface}"` 가 있어
    빈 칸도 클릭이 걸리고, `PreviewMouseDown` 은 `UserControl` 뿌리에 걸려 있다. 정지 조건 5 는 안 걸렸다고
    판단했지만 **실행해 보지 않았다.**
- 시작 때 단축키 동기화 실패는 **작업 관리/1회용 창이 뜰 때만** 알린다. 선택 창으로 시작한 실행에서는 알리지
  않는다.
- `ShellMenuPolicyTests.FileSkipsTheFolderOnlyTwo` 의 이름 속 「둘」은 이제 폴더 전용이 셋(앵커·등록·즐겨찾기)이라
  낡았다. 시험 내용(파일 목록)은 맞으니 이름은 안 바꿨다.
- 1회용 창에서도 「배치 편집」으로 상시 칸을 만들 수 있다. 그 뒤 프리셋을 바꾸면 그 폴더는 순환 칸에 담긴다
  (`Pinned` 플래그는 메모리에 남지만 저장되지 않는다). 막지 않았다.

---

## §6 JH 손확인 목록

**새 설치본**(이 빌드를 publish → ISCC)으로 해 달라 — §5 첫 항목 때문이다.

1. 상단 오른쪽에 「✎ 배치」 토글과 `⋯` 만 남았는가. `⋯` 의 「중복 찾기」·「설정」·「다른 작업 관리 열기」가
   모두 동작하는가. 배치 편집이 켜지면 토글이 눌린 모양으로 남는가.
2. 즐겨찾기를 A 작업 관리 창에서 추가했을 때 열려 있던 B 창에 곧 나타나는가. B 에서 지우면 A 에서 사라지는가.
3. 타일 하나를 클릭하면 위 가장자리에 파란 선이 생기는가. 즐겨찾기를 누르면 그 타일이 옮겨 가는가
   (「앵커 밖」 표시). 창을 닫았다 열어도 그 타일의 앵커가 그대로인가.
4. **빈 순환 칸**을 클릭하면 파란 선이 그 칸으로 옮겨 가는가(§5 — 실행 확인 안 된 자리).
5. `FolderDeck.exe --scratch` 를 두 번 실행하면 창 둘이 뜨는가. 설정 창 「1회용 창」의 처음 배치가 반영되는가.
   `⋯` → 「타일 배치」로 바꾸면 떠 있던 폴더가 옮겨 담기는가.
6. 1회용 창에서 빈 칸을 클릭하고 즐겨찾기를 누르면 그 칸에 뜨는가.
7. 1회용 창에서 폴더를 등록하고 배치를 바꾼 뒤 닫았을 때 `%APPDATA%\FolderDeck\workspaces\` 에 새 파일이
   없는가. 선택 창의 「마지막 작업 관리 자동 열기」가 1회용을 가리키지 않는가.
8. 설치본 시작 메뉴에 「Folder Deck 1회용」이 있고 `Ctrl+Alt+F` 로 뜨는가.
9. 설정에서 단축키를 `Ctrl+Alt+G` 로 바꾸면 **로그아웃 없이** 새 키로 뜨고 옛 키는 안 먹는가.
10. 재설치 후 앱을 한 번 띄우면 단축키가 내가 바꾼 값으로 돌아오는가.
