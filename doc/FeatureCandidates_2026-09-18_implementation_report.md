# 기능 후보 3종 구현 보고 (2026-09-18)

설계는 `doc/FeatureCandidates_2026-09-18_design.md`. 이 문서는 그 설계를 실제로 구현한
결과 — 무엇을 만들었는지, 설계와 어디가 달라졌는지, 시험이 무엇을 못 보는지, 게이트.

지시: "세 후보 모두 설계만 먼저" → 설계 완료 → "하기로 한것만 다 넣어서 순서대로 다
해줘" (검토 게이트 없이 ①→②→③ 순서로 전부 구현). 커밋은 하지 않음 — JH 몫.

## ① 타일 간 비교 (Ctrl+D)

두 타일을 골라 이름 기준으로 대조하고, 각 항목 행에 `[여기만]`/`[다름]` 배지를 붙인다.

- **핵심 로직**: `src/FolderDeck.Core/Comparison/TileComparer.cs` — 순수 함수,
  `CompareStatus`(Unset/Same/Different/OnlyHere) enum과 `CompareEntry` 레코드. 이름
  대소문자 무시 매칭에 `ToLookup` 사용. 7개 시험(`TileComparerTests.cs`), 전부 최초 통과.
- **UI 배선**: `FolderPanelViewModel`에 `CompareRequested` 델리게이트 +
  `StartCompareCommand`(Ctrl+D); `MainViewModel`에 `ComparePeerA`/`ComparePeerB`,
  `RunCompare`, `ApplyCompareBadges`, `ClearCompareCommand`; 타일 삭제 시 비교 피어였으면
  자동 해제. `FolderPanelView.xaml`에 `CompareBadge` DataTemplate 추가, 일반 그리드뷰와
  검색 그리드뷰("이름" 열) 양쪽에 삽입.
- **타일 선택 다이얼로그**: `PickOneDialog` (신규, `Services/IUserPrompt.PickOne(title,
  message, options) → int?`). 문자열 리스트만 받는 범용 다이얼로그로 만들었다 — 처음에는
  `TileViewModel` 리스트를 직접 받는 `TileCompareDialog`로 만들었다가, `Services` 계층이
  `ViewModels` 타입을 참조하게 되는 걸 깨닫고 되돌렸다 (Services→ViewModels 참조 금지는
  이 코드베이스 전역 규약).
- **재동기화 정책**: 피어 타일이 새로고침되면(`LoadCompleted`) 비교 전체를 지운다 —
  "살아있는 재동기화"는 설계 비목표라 조용히 다시 맞추지 않는다.
- **설계 대비 편차**: 설계 문서는 헤더에 체크박스로 "비교 모드"를 켜는 안을 검토했으나,
  최종 구현은 Ctrl+D → 다이얼로그로 상대 타일 선택 방식이다. 이유: 체크박스는 헤더의
  WideOnlyNavButton/NarrowOnlyNavButton 아이콘 열에 자리를 새로 요구하고 (D117–D120에서
  이미 다툰 자리), 다이얼로그 방식은 그 열을 전혀 건드리지 않는다.
- **새 App 시험**: `TileCompareTests.cs` 5개, 전부 최초 통과.

### 시험이 못 보는 것 (손 확인 필요)
- `PickOneDialog`의 실제 레이아웃/버튼 동작 — 자동 시험 없음.
- `CompareBadge`는 일반 GridView와 SearchGridView의 "이름" 열에만 넣었다 — 큰 아이콘/
  아주 큰 아이콘 보기(아이콘 뷰)에는 배지가 전혀 안 뜬다. 그 보기로 전환한 채 비교하면
  기능이 있는지도 모른다. 실사용 시 걸릴 수 있는 실제 범위 제한.
- Ctrl+D가 포커스가 파일 목록 안에 있을 때도 먹는지 — 커맨드 바인딩 자체는 시험했지만
  키 제스처 전달은 손으로 눌러봐야 확실하다.

## ③ 배치 이름 바꾸기 (Ctrl+F2)

여러 항목을 골라 규칙(찾기/바꾸기, 번호 매기기, 대소문자 변경)을 적용, 미리보기 후 실행.

- **핵심 로직**: `src/FolderDeck.Core/Renaming/BatchRenamePlanner.cs` — 순수 함수
  `Preview(originalNames, rule, otherExistingNames?)`, `RenamePreviewRow`(IsBlocked/
  IsUnchanged 계산 속성). 9개 시험 중 1개가 처음 실패했다 — 안 바뀌는 행이 배치 내
  중복 카운트에서 빠져 있어서, 이름이 바뀐 항목이 안 바뀐 형제와 충돌해도 "블록"으로
  안 잡히는 버그였다. 충돌 판정식을 `newNameCounts.GetValueOrDefault(updated) > 1 ||
  (!unchanged && otherNames.Contains(updated))`로 고쳐 24개 Core 시험 전부 통과.
- **다이얼로그**: `BatchRenameDialog` (신규, code-behind 방식 — 라디오 버튼으로 규칙
  선택, 목록이 바뀔 때마다 미리보기 재계산, 블록된 행이 하나라도 있으면 실행 버튼
  비활성화). WPF 초기화 순서 함정을 피하려고 기본 라디오 체크는 XAML이 아니라
  생성자에서 `InitializeComponent()` 이후에 설정했다 (XAML에 두면 `Checked` 핸들러가
  아직 없는 필드를 건드려 죽는다).
- **`IUserPrompt` 배선**: `PlanBatchRename(originalNames, otherExistingNames) →
  IReadOnlyList<RenamePreviewRow>?`. 처음에는 `MainViewModel`이 `BatchRenameDialog`를
  직접 생성했다가, ①의 교훈을 스스로 적용해 `IUserPrompt` 뒤로 옮겼다 — 이번엔 advisor가
  아니라 테스트 가능성을 따지다가 스스로 잡았다.
- **기존 경로 재사용**: `RenameFromItem`에서 `TryRenameLeafOnDisk(item, oldPath, parent,
  oldLeaf, typed)`를 뽑아내 단일 이름 바꾸기와 배치 양쪽이 공유한다. 검증 순서·메시지는
  그대로 — 기존 이름 바꾸기 관련 시험 35개를 다시 돌려 동작이 안 바뀌었음을 확인했다.
  이 추출이 이번 3개 기능 중 가장 위험한 편집이다 — 이미 배포된 경로(F2 단일 이름
  바꾸기)를 건드리기 때문. 35개 회귀 없음으로 확인됨.
- **실패 정책**: `IFileOperationEngine`의 "부분 실패가 기본" 선례를 그대로 따른다 —
  전부 성공/실패가 아니라 항목별로 성공/건너뜀/실패를 모아 보고한다.
- **새 App 시험**: `BatchRenameTests.cs` 6개, 전부 최초 통과.

### 시험이 못 보는 것 (손 확인 필요)
- `BatchRenameDialog`의 실제 미리보기 재계산 타이밍, 라디오 전환 시 화면 갱신, 실행
  버튼 비활성화가 눈에 보이는 색/스타일로 드러나는지 — 자동 시험은 뷰모델 로직만 본다.
- Ctrl+F2가 파일 목록에 포커스가 있을 때 F2(단일 이름 바꾸기)와 충돌 없이 따로
  먹는지 — 커맨드는 별개지만 실제 키 전달은 손으로 확인 안 됨.

## ② 워크스페이스 내 중복 파일 찾기 (도구모음 "중복 찾기")

열린 모든 타일을 훑어 이름+크기가 같은 파일을 묶어 보여준다. 내용까지 같다는 보장은 없다.

- **핵심 로직**: `src/FolderDeck.Core/Comparison/DuplicateFinder.cs` — 순수 함수
  `FindGroups(IEnumerable<(TileLabel, FolderItem)>)`, `(Name.ToUpperInvariant(), Size)`
  기준으로 묶고 파일만 포함, count>1인 것만, 크기 내림차순·이름순 정렬. 6개 시험, 전부
  최초 통과.
- **수집**: `MainViewModel.FindDuplicateGroups()` — 열린 모든 패널을 `Panels.SelectMany`
  + `Tiles.FirstOrDefault`로 타일 라벨을 붙여 순수 함수에 넘긴다. 창을 열지 않고도
  시험 가능.
- **창**: `DuplicateFinderWindow` (신규) — **비모달**(`Show()`, `ShowDialog()` 아님) —
  결과를 보면서 타일도 계속 써야 하기 때문. `TreeView`로 그룹→항목 표시, 항목
  더블클릭 시 기존 `IShellLauncher.RevealInExplorer` 재사용해 탐색기로 연다.
  삭제·정리 등 실제 조작은 V1 비목표 — 사용자가 직접 하거나 기존 파일 조작 기능을 쓴다.
- **진입점**: 도구모음에 새 버튼 "중복 찾기" 추가 (`MainWindow.xaml`, "설정" 버튼 뒤).
  헤더 반응형 아이콘 열(WideOnlyNavButton/NarrowOnlyNavButton) 시스템과는 별개인
  일반 도구모음이라, ①·③과 달리 새 버튼을 넣어도 290px 임계값 다툼과 무관하다.
- **설계 대비 편차**: 설계 문서 §4.12는 새 타일 종류(`kind: duplicates`)로 만드는 안도
  검토했으나, 최종 구현은 별도 `Window`다. 이유: 새 `kind`는 워크스페이스 스키마(v9)
  확장과 직렬화 지원이 필요해 범위가 커진다 — `Window`는 그 부담이 전혀 없다. 결과 창은
  워크스페이스에 저장되지 않고, 앱을 껐다 켜면 사라진다 — 이건 손실이 아니라 의도된
  가벼움이다.
- **새 App 시험**: `FindDuplicateGroupsTests.cs` 3개, 전부 최초 통과.

### 시험이 못 보는 것 (손 확인 필요)
- `DuplicateFinderWindow`의 실제 `TreeView` 렌더링, 더블클릭이 실제로 탐색기를 여는지 —
  자동 시험은 `FindDuplicateGroups()`(수집 로직)만 본다, 창 자체는 안 연다.
- 큰 워크스페이스(타일 여러 개, 파일 수천 개)에서 체감 속도 — 측정 안 함.

## 게이트

- **시험**: Core 334/334, App 917/917 — 전부 통과, 실패 0.
  - 기준선(이번 라운드 시작 전) 대비 순증가: Core +24(310→334), App +14(903→917).
  - Core 신규: `--list-tests`로 실측 — TileComparerTests 7, DuplicateFinderTests 6,
    BatchRenamePlannerTests 11(이론 데이터 행 포함, `[Theory]` 메서드 수 9개가 실제
    케이스로는 11개), 합 24 — 310+24=334로 정확히 일치.
  - App 신규: TileCompareTests 5, BatchRenameTests 6, FindDuplicateGroupsTests 3, 합
    14 — 903+14=917로 정확히 일치.
  - 기존(배치 추가 이전) 이름 바꾸기 관련 App 시험 재실행 — `dotnet test
    tests/FolderDeck.App.Tests --filter "FullyQualifiedName~Rename&FullyQualifiedName!~BatchRename"`
    로 35개 전부 통과 확인, `RenameFromItem` 리팩터링이 동작을 안 바꿨다는 증거.
- **빌드**: `dotnet build` 0 오류. 중간에 `CS0136`(중복 변수 `reason`) 1건 발생 →
  `conflictReason`으로 개명해 해결.
- **git 상태**: `git status --porcelain -- src tests doc` 기준 위 파일들만 걸림 (수정
  8개 + 신규 15개, `doc/`의 이 보고서 자신 포함하면 16개). 커밋 없음 — JH 몫.
- **손 확인**: 이번 라운드는 GUI를 직접 켜서 확인하지 못했다 — 위 세 절의 "시험이 못
  보는 것" 항목 전부가 미확인 상태다. 다이얼로그 레이아웃·배지 색·키 바인딩 충돌 여부는
  JH가 손으로 확인해야 한다.
