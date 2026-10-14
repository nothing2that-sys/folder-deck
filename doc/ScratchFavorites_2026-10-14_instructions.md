# FolderDeck — 1회용 탐색기 · 공유 즐겨찾기 · 배치 프리셋 · 상단 메뉴 정리 (2026-10-14)

작성 Claude · **실행 코웍(Claude Code)** · 성격 **구현 회차(4단계)**
저장소 `<repository-root>`

---

## §0 커밋 (JH 가 직접 실행 — 코웍에 넘기기 전)

```
git add doc/ScratchFavorites_2026-10-14_instructions.md
git commit -m "doc: instructions for scratch mode, shared favorites, layout presets, toolbar menu"
```

`doc/USER_MANUAL.md` 는 이 회차와 무관한 미추적 파일이다. 같이 올리지 마라.

---

## §1 코웍에게 — 이 저장소의 규칙

**하지 않는 것**

- **커밋하지 않는다.** `git commit` 은 JH 만 한다.
- **금지 명령** — `git add -A` · `git add -a` · `git stash` · `git checkout --` · `git restore`.
- **이 지시서가 정한 범위 밖의 기능을 손대지 않는다.** 지나가다 본 버그는 보고문 「범위 밖 발견」에
  적기만 한다.
- **기존 시험의 기대값을 바꾸지 않는다.** 기존 시험이 깨지면 §7 정지 조건 4 로 간다.
- **버전을 올리지 않는다**(`Directory.Build.props`·`installer/FolderDeck.iss` 의 버전). 릴리스는 따로 한다.

**지키는 것**

- 단계(S1→S4)마다 **빌드 + 전체 시험**을 돌리고 통과 수를 보고문에 적는다. 다음 단계는 앞 단계가
  초록일 때만 간다.
- **새 창·다이얼로그를 만들거나 XAML 에 이벤트 걸린 컨트롤을 더하면** STA 생성 시험을 하나 이상
  더한다(`tests/FolderDeck.App.Tests/StaWpfTestSupport.cs`). 이벤트가 걸린 프로퍼티(`IsChecked`·
  `Text`·`SelectedIndex` 등)에 XAML 기본값을 주지 않는다 — 생성자에서 필드 대입이 끝난 뒤 코드로
  준다(2026-09-20 `BatchRenameDialog` 크래시와 같은 함정).
- 저장 형식을 바꾸면 그 모델의 `CurrentSchemaVersion` 주석에 버전 이력 한 줄을 이 저장소 관례대로
  덧붙인다(`Workspace.cs` 의 2~11 이력 참고).
- 보고문은 한국어, CRLF. 인용은 원문 그대로 + 경로:줄.

---

## §2 배경 — JH 가 요청한 것과 정한 것

요청(2026-10-14, 원문 요약):

1. 윈도우 탐색기처럼 단축키 한 번으로 뜨는 **휘발성 1회용 탐색기 모드**.
2. 탐색기 즐겨찾기처럼 **모든 작업 관리가 공유하는 즐겨찾기 목록**.
3. 1회용 모드의 **타일 구성 프리셋** — 1개 전체 · 좌우 · 상하 · 4개.
4. 상단 오른쪽 「중복 찾기」「설정」「배치 편집」「다른 작업 관리 열기」가 자리를 너무 차지한다 —
   작게 줄이거나 옮긴다.

JH 결정(2026-10-14):

- **단축키는 A 방식** — 상주 프로세스를 만들지 않는다. 시작 메뉴 바로가기(.lnk)의 바로 가기 키로
  `FolderDeck.exe --scratch` 를 띄운다.
- **단축키는 앱 설정에서 바꿀 수 있어야 한다**(JH 추가 요청). 설치가 사용자 단위
  (`installer/FolderDeck.iss` 의 `PrivilegesRequired=lowest`)라 시작 메뉴 바로가기가 사용자 폴더에
  있고, 앱이 관리자 권한 없이 그 .lnk 의 바로 가기 키를 고쳐 쓸 수 있다.
- **1회용 창은 여러 개 동시에 허용**한다.
- **즐겨찾기는 등록 폴더와 분리**한다 — 즐겨찾기는 「바로 이동」 목록일 뿐이고, 작업 관리별 상태
  (보기 방식·타일 배치)를 갖는 등록 폴더와 섞지 않는다.

---

## §3 확정 결정 (이 회차에서 바꾸지 않는다)

| 항목 | 값 | 근거 |
|---|---|---|
| 단계 순서 | S1 상단 메뉴 → S2 배치 프리셋 → S3 즐겨찾기 → S4 1회용 모드 | 작고 독립적인 것부터. S4 는 S2·S3 위에 선다 |
| 1회용 진입 | 새 인자 `--scratch` | `CommandLineOptions` 에 스위치 하나 더하는 것뿐 |
| 단축키 변경 | 설정값(`AppSettings`)이 원본. 앱이 시작 메뉴 .lnk 의 바로 가기 키를 그 값으로 맞춘다 | 재설치하면 Inno 가 .lnk 를 기본값으로 다시 만든다 — 설정을 원본으로 두고 앱 시작 때 다시 맞춰야 사용자 변경이 살아남는다 |
| 1회용 인스턴스 | 뮤텍스·파이프를 **잡지 않는다** | 여러 개 허용(JH). `InstanceLock`/`ActivationListener` 는 작업 관리 전용으로 남긴다 |
| 1회용 저장 | 작업 관리 JSON 을 **한 바이트도 쓰지 않는다** | 휘발성. 설정·즐겨찾기 저장은 그대로 한다 |
| 즐겨찾기 저장 위치 | `%APPDATA%\FolderDeck\favorites.json` (새 파일, 자체 `schemaVersion` 1) | `settings.json` 에 넣으면 기존 설정 저장과 덮어쓰기 경쟁을 공유하게 된다 |
| 즐겨찾기 동시성 | 변경 = **이름 있는 뮤텍스 안에서 디스크 다시 읽기 → 적용 → 원자적 쓰기** | 작업 관리마다 프로세스가 따로라 「메모리 상태 통째 쓰기」는 다른 창의 변경을 지운다 |
| 즐겨찾기 클릭(일반 모드) | **대상 타일**을 그 경로로 임시 이동(`NavigateToAsync`). 저장 안 함 · 앵커 불변 | 분리(JH). 기존 「앵커 밖」 표시를 그대로 쓴다 |
| 배치 프리셋 적용 범위 | **1회용 모드에서만** | 일반 작업 관리에 넣으면 저장된 배치를 한 번에 날리는 버튼이 생긴다 — 이번엔 안 연다 |
| 프리셋 구현 | 4×4 격자 위의 칸 걸침(span). 렌더러 안 고침 | `GridLayout.EnsureGrid` 가 4×4 고정이다 |
| 버전 | 올리지 않음 | 릴리스 회차가 따로 한다 |

---

## §4 시작 게이트

저장소 루트에서 `git -C <루트>` 로 잰다. 실측값을 보고문 §1 에 적는다. 어긋나면 **멈추고 JH 에게
알린다.**

| 항목 | 기대값 |
|---|---|
| `git rev-parse HEAD:src` | `9cd2fc7d865523d54033b19e3e5ecfcaed19da27` |
| `git rev-parse HEAD:tests` | `e68a893790f6c5f9d5a92ea407809f5f5470bfe7` |
| `git status --porcelain -u -- src tests` | 빈 출력 |

`HEAD` 자체와 `doc/` 상태는 게이트가 아니다(§0 커밋이 HEAD 를 옮기고, `doc/USER_MANUAL.md` 가
미추적으로 남아 있다). **기록만** 한다.

시작 전 `dotnet build` + `dotnet test` 를 한 번 돌려 **기준 통과 수**를 적는다(게이트 아님, 기준값).

---

## §5 과제

### S1. 상단 오른쪽 버튼을 메뉴 하나로 접는다

**지금** — `src/FolderDeck.App/MainWindow.xaml` 상단 `DockPanel` 오른쪽에 글자 버튼 넷이 있다:
「다른 작업 관리 열기」(`OpenLauncherCommand`) · 「배치 편집」(`ToggleLayoutEditCommand`,
`EditModeButton` 스타일) · 「설정」(`OpenSettingsCommand`, `IsEnabled="{Binding CanOpenSettings}"`) ·
「중복 찾기」(`FindDuplicatesCommand`). 닻은 줄 번호가 아니라 `Content="다른 작업 관리 열기"` 같은
텍스트로 찾는다.

**바꿀 것**

1. 오른쪽 끝에 **`⋯` 버튼 하나**(기존 `NavButton` 스타일)를 두고, 누르면 메뉴가 뜬다. 메뉴 항목:
   「중복 찾기」 · 「설정」 · 구분선 · 「다른 작업 관리 열기」. 각 항목은 기존 명령을 그대로 바인딩한다.
   「설정」의 `CanOpenSettings` 비활성 조건을 메뉴 항목에도 그대로 건다.
2. **「배치 편집」은 메뉴에 넣지 않는다** — 켜짐/꺼짐이 보여야 하는 모드다. 버튼으로 남기되 폭을
   줄인다(아이콘 + 툴팁, 또는 짧은 글자). 켜진 상태가 지금처럼 눈에 띄어야 한다
   (`EditModeButton` 스타일의 켜짐 표시를 유지).
3. 기존 버튼들의 `AutomationProperties.Name`·`ToolTip` 문구는 메뉴 항목으로 옮겨 살린다.
4. XAML 주석의 「오른쪽 끝 둘 (7)」 등 배치 설명을 새 구조에 맞게 고쳐 쓴다(죽은 설명을 남기지 않는다).

**메뉴를 띄우는 방식** — 버튼 클릭으로 `ContextMenu` 를 여는 코드 비하인드가 필요하면 쓴다.
`ContextMenu` 는 시각 트리 밖이라 `DataContext` 가 안 이어진다 — `PlacementTarget` 로 이어 주는 것을
잊지 마라(메뉴 항목 명령이 null 로 묶이면 클릭해도 아무 일도 안 일어나고 시험도 안 잡는다).

**시험** — `MainWindowFixture` 로 창을 STA 생성해서, ⋯ 메뉴의 항목 셋이 **null 이 아닌 명령**에
묶였는지 확인하는 시험 하나.

### S2. 배치 프리셋 (Core 순수 함수)

`src/FolderDeck.Core/Layout/` 에 새 파일(`LayoutPresets.cs` 등)로 둔다.

```
public enum LayoutPreset { Single, SideBySide, Stacked, Quad }
public static List<TileSpec> Build(LayoutPreset preset)   // 4x4 기준
```

| 프리셋 | 타일(CellX, CellY, SpanX, SpanY) | RotationIndex |
|---|---|---|
| `Single` 1개 전체 | (0,0,4,4) | 1 |
| `SideBySide` 좌우 | (0,0,2,4) · (2,0,2,4) | 1 · 2 |
| `Stacked` 상하 | (0,0,4,2) · (0,2,4,2) | 1 · 2 |
| `Quad` 4개 | (0,0,2,2) · (2,0,2,2) · (0,2,2,2) · (2,2,2,2) | 1 · 2 · 3 · 4 |

- 타일은 **전부 `TileKind.Rotating`, `FolderId = null`**. 근거 — `GridLayout.NormalizeTiles` 가
  `Pinned` 이면서 폴더가 없는 타일을 지운다(`if (tile.Kind == TileKind.Pinned && (tile.FolderId is null ...`).
  빈 칸으로 설 수 있는 것은 순환 칸뿐이다.
- 순서는 읽기 순서(왼→오, 위→아래)이고 `RotationIndex` 도 그 순서다.

**설정** — `AppSettings` 에 `ScratchLayout`(기본값 `SideBySide`) 을 더한다. 필드가 늘었으니
`AppSettings.CurrentSchemaVersion` 을 하나 올리고 이력 주석을 단다. 범위 밖 값은 기본값으로 떨어진다.
설정 창에 「1회용 창 기본 배치」 선택을 더한다(이벤트 걸린 `ComboBox` 면 §1 의 초기화 순서 규칙).

**시험(Core)** — 네 프리셋 각각: 타일 수 · 좌표 표 일치 · 모두 `GridLayout.IsInside` 참 · 서로
`Overlaps` 거짓 · `Workspace` 에 넣고 `NormalizeTiles` 를 돌려도 **하나도 안 지워진다**(0 반환).
마지막 항목이 「순환 칸이어야 한다」는 결정을 실물로 재는 시험이다 — 빼지 마라.

### S3. 공유 즐겨찾기

**모델·저장(Core)**

- `FavoriteEntry { Guid Id; string Path; string? DisplayName; }`
- `FavoritesStore` — `%APPDATA%\FolderDeck\favorites.json`. 경로는 `FolderDeckPaths` 에
  `FavoritesFile` 로 더한다.
  - `Load()` → `StorageResult<List<FavoriteEntry>>`. 파일 없음 = 빈 목록(실패 아님).
    손상 = 실패로 보고(기존 `StorageResult` 관례).
  - `Mutate(Func<List<FavoriteEntry>, bool> change)` → `StorageResult<List<FavoriteEntry>>` —
    **이름 있는 뮤텍스**(`Local\FolderDeck.Favorites`, 이름은 `InstanceNames` 관례를 따른다) 안에서
    ① 디스크에서 다시 읽고 ② `change` 를 적용하고 ③ `AtomicFile` 로 쓴다. `change` 가 false 면
    쓰지 않는다.
  - **손상된 파일 위에는 쓰지 않는다** — `Mutate` 가 실패를 돌려주고 파일은 그대로 둔다. 사용자가
    고칠 길을 지우지 않는다(§5 「실패를 삼키지 않는다」).
  - 같은 경로 중복 추가는 거부한다(`FolderPathRules.Normalize` 로 비교).

**다른 창의 변경 반영(App)** — `favorites.json` 을 `FileSystemWatcher` 로 보고, 바뀌면 짧게
디바운스한 뒤 UI 스레드에서 다시 읽는다. 자기 쓰기로 생긴 알림이 와도 다시 읽기만 하므로 무해하다.

**왼쪽 패널(App)**

- 왼쪽 패널 **맨 위**에 「즐겨찾기」 묶음을 둔다. 기존 묶음 헤더처럼 접을 수 있다. 접힘 상태는
  즐겨찾기가 전역이므로 **`AppSettings` 에** 둔다(작업 관리 JSON 의 `LeftLayout` 을 건드리지 않는다 —
  건드리면 `Workspace` 스키마가 오른다).
- 추가 경로 셋: 묶음 헤더의 `＋`(폴더 고르기 — 기존 `FolderPicker`) · 파일 목록에서 폴더 항목
  우클릭 「즐겨찾기에 추가」 · 타일 헤더(또는 현재 폴더 메뉴)의 「이 폴더를 즐겨찾기에 추가」.
- 즐겨찾기 행 우클릭: 「제거」 · 「위로」 · 「아래로」 · 「등록 폴더로 추가」(기존 `RegisterFolderAtPath`
  를 부른다 — 사용자가 명시적으로 고를 때만 등록 폴더와 이어진다).
- 경로가 지금 없으면 흐리게 보이고, 클릭하면 이유를 한 줄로 알린다(지우지 않는다).

**클릭 동작 — 「대상 타일」**

- `MainViewModel` 에 런타임 전용 상태 **대상 타일**을 둔다: 마지막으로 사용자가 클릭해 포커스한
  타일. 저장하지 않는다. 포커스 판정은 기존 `PanelFocusPolicy` 가 패널에 포커스를 주는 자리에
  얹는다. 대상 타일은 화면에 표시한다(테두리 강조 등, 과하지 않게).
- 대상 타일이 없으면 `RotationIndex` 1 인 순환 칸을 대상으로 본다.
- 클릭 시:
  - 대상 타일 패널에 폴더가 떠 있다(`Entry` 있음) → `NavigateToAsync(path)`. **임시 이동**이다 —
    앵커도 작업 관리 JSON 도 안 바뀐다(`FolderPanelViewModel` 주석의 「하위 진입·상위 이동은
    CurrentPath 만 바꾸며 저장하지 않는다」 그대로).
  - 대상 타일이 비었다 → **일반 모드**: 「이 칸은 비어 있다 — 등록 폴더를 먼저 띄우거나 우클릭
    「등록 폴더로 추가」를 써라」 한 줄 안내. **1회용 모드**: S4 의 규칙을 따른다.

**시험**

- Core: 왕복(추가→Load) · 중복 거부 · **두 `FavoritesStore` 인스턴스가 번갈아 `Mutate` 해도 둘의
  추가가 모두 남는다**(이 회차의 핵심 불변 — 낡은 메모리 상태로 덮어쓰지 않음을 실물로 잰다) ·
  손상 파일 위 `Mutate` 는 실패하고 **파일 바이트가 그대로다**(바이트 비교).
- App: 일반 모드에서 즐겨찾기 클릭이 대상 타일을 이동시키고 **`Workspace.Folders` 개수가 그대로**이며
  저장소에 작업 관리 저장이 **예약되지 않는다**(기존 테스트 더블로 센다).

### S4. 1회용 모드 (`--scratch`)

**진입**

- `CommandLineOptions` 에 `ScratchSwitch = "--scratch"` 와 `Scratch` 속성을 더한다.
  `--scratch` 와 `--workspace` 를 같이 주면 기존 `Error` 관례로 오류를 내고 선택 창으로 떨어진다.
- `App.OnStartup` — `options.Scratch` 면 `SkipLauncher` 와 상관없이 1회용 창을 연다.

**1회용 작업 관리**

- 메모리에서만 만든다: `new Workspace { Title = "1회용", Folders = [], Tiles = LayoutPresets.Build(설정값) }`.
- **저장 차단** — `IWorkspaceStore` 를 감싸는 얇은 래퍼(예: `ScratchWorkspaceStore`)로
  `SaveWorkspace`·`SaveWorkspaceDebounced` 를 이 작업 관리에 대해 아무것도 안 하게 한다. 설정 저장·
  `Flush`·`Paths` 는 안쪽 저장소로 넘긴다. `MainViewModel` 에 「저장 안 함」 플래그를 흩뿌리지 말고
  이 래퍼 하나로 끝낸다 — 저장 호출 자리가 `MainViewModel` 안에만 셋 이상이라(`Save()`,
  `SaveWorkspaceDebounced` 직접 호출) 플래그 방식은 하나를 빠뜨린다.
- **뮤텍스·파이프를 잡지 않는다** — `App.Open` 의 `InstanceLock.TryAcquire` · `StartListener` 경로를
  타지 않는 별도 경로로 띄운다. `LastWorkspaceId` 도 바꾸지 않는다(`ShowWorkspaceWindow` 앞부분 —
  1회용의 Guid 가 「마지막 작업 관리」로 남으면 다음 자동 열기가 없는 파일을 찾는다).
- 창 제목에 「1회용」이 드러나게 한다. 창 위치는 기본 위치(`Workspace.Window` null).

**1회용 창의 화면**

- S1 의 ⋯ 메뉴에 **1회용 모드에서만** 「타일 배치 ▸ 1개 전체 / 좌우 / 상하 / 4개」 하위 메뉴를
  더한다. 고르면 그 자리에서 타일을 다시 만든다(확인 묻지 않음 — 휘발성 창이다).
  다시 만들 때 **떠 있던 폴더를 읽기 순서대로 새 칸에 옮겨 담고**, 칸이 모자라 넘치는 것은 칸에서만
  빠진다(왼쪽 목록에는 남는다).
- 대상 타일이 **빈 칸**일 때 즐겨찾기를 클릭하면: 그 경로로 `FolderEntry` 를 메모리에서 등록하고
  (`Pinned = false`, `RotationSlot` = 대상 칸의 `RotationIndex`) 기존 `ShowFolderAsync` 로 띄운다.
  이 등록은 래퍼가 저장을 막으므로 디스크에 남지 않는다.
- 「다른 작업 관리 열기」는 그대로 둔다.

**설치 프로그램** — `installer/FolderDeck.iss` `[Icons]` 에 한 줄 더한다:

```
Name: "{group}\Folder Deck 1회용"; Filename: "{app}\FolderDeck.exe"; Parameters: "--scratch"; HotKey: ctrl+alt+f
```

ISCC 가 이 PC 에 있으면 컴파일만 해 보고 결과를 적는다. 없으면 「컴파일 안 함」으로 적는다(지어내지
마라). `ctrl+alt+f` 는 **처음 설치했을 때의 기본값**일 뿐이고, 이후에는 아래 「단축키 변경」이 원본이다.

**단축키 변경**

- `AppSettings` 에 `ScratchHotkey` 를 더한다(기본 `Ctrl+Alt+F`, 「없음」 허용). S2 에서 이미
  `CurrentSchemaVersion` 을 올렸으면 한 번 더 올리지 말고 **같은 버전 이력 줄에 함께 적는다**.
- 설정 창에 「1회용 창 단축키」 칸을 둔다 — 키를 눌러 잡는 입력칸 + 「없음」. .lnk 바로 가기 키가
  받는 조합만 허용한다: **`Ctrl+Alt`(선택적으로 `+Shift`) + 영문자 · 숫자 · F1~F12**. 그 밖의 조합은
  저장하지 않고 이유를 한 줄로 알린다. (이벤트 걸린 입력칸이면 §1 초기화 순서 규칙.)
- 적용은 `IShellLinkW` + `IPersistFile` COM 인터롭으로 .lnk 의 `Hotkey` 를 쓰고 저장한다. 대상은
  `Environment.SpecialFolder.Programs` 아래 `Folder Deck\Folder Deck 1회용.lnk` 이다.
  - **설정 저장 때** 적용한다.
  - **앱 시작 때**(작업 관리·선택 창·1회용 어느 경로든) .lnk 의 키가 설정과 다르면 다시 맞춘다 —
    재설치가 기본값으로 되돌려 놓은 것을 고친다. 시작을 막지 않게 실패는 조용히 넘기지 말고 상태
    줄 한 줄로만 알린다.
  - **.lnk 가 없으면 만들지 않는다**(개발 빌드 · 사용자가 지운 경우). 설정 창에 「설치본 바로가기가
    없어 적용할 수 없다」고 보인다. 앱이 만든 .lnk 는 제거 프로그램이 모르는 파일이 되기 때문이다.
- 다른 앱과 키가 겹치는지는 **판정할 수 없다**(전역 단축키 등록 목록을 읽는 공개 API 가 없다).
  설정 창 도움말에 「다른 프로그램과 겹치면 먼저 등록된 쪽이 이긴다」 한 줄만 둔다.
- 매뉴얼 반영은 이 회차 밖이다.

**시험**

- `CommandLineOptions`: `--scratch` 단독 · `--scratch --workspace {guid}` 오류.
- 저장 차단: 임시 루트의 `WorkspaceStore` 를 래퍼로 감싸 1회용 `MainViewModel` 을 만들고, 폴더 등록·
  타일 변경을 한 뒤 `Flush` 해도 **`workspaces\` 아래 파일 수가 0** 인지 센다. 설정 저장은 여전히
  된다는 것도 같이 확인한다. (래퍼를 흉내 낸 가짜로 재지 말고 실물 `WorkspaceStore` 를 감싸서 재라.)
- 프리셋 전환: 폴더 둘이 떠 있는 `Quad` → `Single` 전환 시 첫 폴더가 남고 둘째가 칸에서 빠지며
  `Workspace.Folders` 는 그대로 둘이다.
- 1회용 `MainWindow` STA 생성 시험 하나.
- 단축키: 임시 폴더에 **실물 .lnk** 를 만들어 키를 쓰고 다시 읽어 같은지 · 「없음」이면 키가 0 인지 ·
  허용 안 되는 조합(예: `Ctrl+F`, `Alt+Space`)을 거부하는지. 키 조합 ↔ .lnk 키 값 변환은 순수
  함수로 빼서 따로 시험한다. 설정 창 STA 생성 시험은 칸을 더한 뒤 다시 통과해야 한다.

---

## §6 비목표 (이 회차에서 하지 않는다)

- 트레이 상주 · `RegisterHotKey` · `Win+E` 대체.
- 1회용 창을 작업 관리로 「저장/승격」.
- 일반 작업 관리의 배치 프리셋.
- 즐겨찾기 끌어놓기 순서 변경 · 이름 바꾸기 · 폴더 묶음(하위 그룹).
- 즐겨찾기를 타일로 끌어다 놓기.
- 사용자 매뉴얼·릴리스 노트·버전 올림.

---

## §7 정지 조건

여기 적힌 것 말고 스스로 판단해 범위를 넓히지 마라.

1. **시작 게이트 어긋남** → 아무것도 고치지 않고 멈춘다.
2. **빌드 실패** → 한 단계 안에서 **세 번까지** 고친다. 세 번에 안 되면 마지막 오류 전문을 적고 그
   단계에서 멈춘다. 앞 단계 결과는 남긴다.
3. **일반 모드 즐겨찾기 클릭을 구현하는 데 `Workspace.Folders` 나 작업 관리 JSON 을 바꿔야만 한다**
   → 분리 결정(JH)과 충돌한다. 그 자리에서 멈추고 왜 그런지 코드 인용으로 적는다.
4. **기존 시험이 깨지고, 고치려면 그 시험의 기대값을 바꿔야 한다** → 멈추고 어느 시험·어느 불변인지
   적는다.
5. **빈 순환 칸이 포커스를 못 받아 대상 타일이 될 수 없다** → 멈추지 말고 「대상 없음 → 순환1」
   폴백으로만 구현하고, 막힌 이유를 보고문에 적는다. JH 가 다음 회차에서 정한다.
6. **1회용 창이 뮤텍스·파이프 없이 띄워지지 않는다**(공유 코드가 둘을 전제한다) → 멈추고 전제하는
   자리를 인용해 적는다.
7. **.lnk 키 쓰기가 시험 환경에서 COM 오류로 안 된다** → 두 번까지 고친다. 그래도 안 되면 단축키
   변경만 빼고(설정 칸은 「적용 불가」 표시) 나머지 S4 를 끝낸 뒤 오류 전문을 적는다.

---

## §8 산출물

- 코드·시험 변경(커밋하지 않는다). 바뀐 파일 목록은 `git status --porcelain -u -- src tests installer`
  출력 그대로.
- 보고문 `doc/ScratchFavorites_2026-10-14_report.md`:
  - §1 시작 게이트 실측 · 기준 시험 통과 수
  - §2~§5 단계별: 한 일 · 새 시험 이름 · 빌드/시험 통과 수(단계 끝마다)
  - §6 지시서가 틀린 자리(있으면)
  - §7 범위 밖 발견
  - §8 JH 손확인 목록(아래 §9 를 실행 결과에 맞게 다듬어서)
- **핸드오프는 네가 쓰지 않는다.**

---

## §9 JH 손확인 (회차 끝, 참고용)

1. 상단 오른쪽이 `⋯` + 배치 편집 토글만 남았는가. 메뉴 세 항목이 모두 동작하는가.
2. 즐겨찾기를 A 작업 관리 창에서 추가 → 열려 있던 B 창에 곧 나타나는가. B 에서 지우면 A 에서 사라지는가.
3. 즐겨찾기 클릭이 대상 타일을 옮기고, 창을 닫았다 열어도 그 타일의 앵커가 그대로인가.
4. `FolderDeck.exe --scratch` 를 두 번 → 창 둘이 뜨는가. 설정 창의 기본 배치가 반영되는가.
5. 1회용 창에서 폴더를 등록·배치를 바꾼 뒤 닫고 `%APPDATA%\FolderDeck\workspaces\` 에 새 파일이 없는가.
6. 설치본 시작 메뉴 바로가기 `Ctrl+Alt+F` 로 1회용 창이 뜨는가.
7. 설정에서 단축키를 `Ctrl+Alt+G` 로 바꾸면 **로그아웃 없이** 새 키로 뜨고 옛 키는 안 먹는가.
   (탐색기가 .lnk 변경을 바로 읽는지는 자동 시험이 못 잰다 — 이 항목이 유일한 확인이다.)
8. 재설치 후 앱을 한 번 띄우면 단축키가 내가 바꾼 값으로 돌아오는가.
