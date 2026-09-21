# 손 확인 라운드 — 실제 버그 5건 + 표시 개선 2건 (2026-09-20 ~ 09-21)

`doc/FeatureCandidates_2026-09-18_implementation_report.md`(`7db4b64`)와
`doc/ContentCompare_2026-09-18_report.md`(`17635f1`)는 이미 커밋됐고 그 규약대로 손대지
않는다 — 이 문서는 JH가 손으로 확인해보다 실제로 발견한 문제와 그 수정을 다룬다(2026-09-20
에 3건, 09-21 후속 확인에서 버그 2건 + JH가 그 자리에서 요청한 표시 개선 2건 — 6번은
**가설이었으나 09-21 재확인으로 맞았음이 확인됨**, 타일 비교 배지가 양쪽 다 뜬다). 버그
다섯은 "자동 시험은 초록인데 실물은 안 됐던" 자리 — XAML 배선·기본 화면·다이얼로그 기본
선택·창 활성화 경쟁처럼 자동 시험이 원천적으로 못 무는 곳에서 났다.

## 1. 타일 비교 배지가 안 보임 — 기본 보기(목록)에 배지가 아예 없었다

**증상(JH 보고):** 비교 다이얼로그까지는 뜨는데 그 이후 아무것도 안 보인다.

**원인:** `CompareBadge`는 "자세히 보기"(`NormalGridView`)와 검색 결과 화면에만 넣었다.
그런데 `FolderEntry.ViewMode`의 **기본값은 "목록"**(`FolderEntry.cs:21`)이고, 목록 보기는
완전히 별도의 `ListView.ItemTemplate`(`FolderPanelView.xaml:1104` 부근)을 쓴다 — 거기엔
배지 `ContentControl`이 처음부터 없었다. 즉 처음 만든 타일은 전부 목록 보기로 열리므로,
자세히 보기로 직접 바꿔보지 않는 한 배지 기능 자체가 없는 것과 같았다.

**고침:** 목록 보기 `ItemTemplate`에도 같은 `CompareBadge` `ContentControl`을 추가했다
(`FolderPanelView.xaml`). 이제 목록·자세히·검색 결과 셋 다에서 배지가 뜬다. 큰 아이콘/
아주 큰 아이콘 보기는 여전히 의도적으로 제외(처음부터 알려진 범위 제한, 변경 없음).

**시험:** XAML 템플릿 추가라 자동 시험이 못 무는 자리다(뷰모델의 `CompareStatus` 값 자체는
`TileCompareTests`가 이미 덮고 있었다 — 문제는 그 값을 "그리는" 쪽에만 있었다). 손 확인
필요.

## 2. 배치 리네임(Ctrl+F2) 크래시 — 실제로 재현·수정·회귀 시험까지 확인

**증상(JH 보고):** 파일 여러 개 선택하고 Ctrl+F2 누르면 프로그램이 죽는다.

**원인(크래시 로그로 확정, `%APPDATA%\FolderDeck\crashes\`):**
`BatchRenameDialog.xaml`의 `StartBox`(시작 번호)·`PaddingBox`(자릿수)에 XAML로 박아둔
기본값(`Text="1"`, `Text="3"`)이 `InitializeComponent()` **도중** `TextChanged`를 쏘는데,
그 시점은 생성자가 아직 `_originalNames`를 대입하기 전이다 — `Recompute()`가
`BatchRenamePlanner.Preview(null, ...)`를 불러 `ArgumentNullException`으로 죽고, 그게
`XamlParseException`에 싸여 `DispatcherUnhandledException`→`AppDomain.UnhandledException`
까지 그대로 올라가 앱이 종료됐다. 라디오 버튼은 이 함정(D117류 초기화 순서 문제)을 이미
피해뒀는데 텍스트박스 둘을 놓쳤다 — **선택한 파일 개수와 무관하게 다이얼로그를 열기만
해도 매번 났을 결함**이다.

**고침:** XAML 기본값을 지우고 생성자에서(`InitializeComponent()` + 필드 대입 이후) 채우는
것으로 통일 — 라디오 버튼과 같은 자리에서 같이 설정한다.

**시험:** `BatchRenameDialogConstructionTests.cs`(신규) — 실제 STA 스레드에서
`BatchRenameDialog`를 진짜로 생성해본다. **고치기 전 코드로 되돌려 이 시험이 실제로
실패하는 것까지 확인**(`Assert.Equal() Failure`가 아니라 원래 크래시와 똑같은
`ArgumentNullException`/`XamlParseException`이 남), 고친 뒤 다시 통과 확인. 이런 창들
(`PickOneDialog`·`BatchRenameDialog`·`DuplicateFinderWindow`)은 지금까지 생성 자체를
시험한 적이 없었다 — 정확히 그 사각지대에서 났다.

## 3. 중복 찾기 화면 재설계 — TreeView → 표(GridView)

**증상(JH 보고):** 뭔가 뜨긴 하는데 직관적이지 않고 어렵다.

**진단:** `TreeView`(들여쓰기+전체 경로 텍스트)는 이 앱 어디에도 없는 스타일이다 — 파일
목록·배치 리네임 미리보기 등 다른 화면은 전부 GridView(표) 어휘를 쓴다. 또 각 항목에
전체 경로를 통째로 붙여놔서 줄이 길고 산만했다.

**JH 선택(질문 제시 후):** "표(GridView) 형태로 전면 교체."

**새 구조:** 파일명·크기는 **그룹 머리글 하나에만** 나온다(그룹 안 모든 행이 같은 값이라
반복 안 함) — 행마다는 "이 사본이 어디 있는가"(위치=타일·경로=폴더만·수정일)만 다르게
보여준다. `ListView.GroupStyle`로 그룹 머리글을 얹었다.

**advisor가 잡은 실제 버그:** 처음 구현은 그룹을 **화면에 보일 문자열**
(`"이름 · Humanize(크기) — N곳"`)로 직접 갈랐다. `Humanize`는 정밀도를 버리는 함수라
(1024·1025바이트가 둘 다 "1 KB") 이름이 같고 **정확한 바이트 수만 다른 두 개의 진짜
다른 그룹**(`DuplicateFinder`는 이미 이름+정확한 크기로 갈라 놓았다)이 화면에서 하나로
합쳐 보일 뻔했다. `DuplicateGroupHeading`(이름·정확한 크기·개수로 동등성을 판단하는
구조체, `ToString()`만 사람이 읽는 문자열)을 만들어 **그룹 정체성과 표시 문자열을
분리**했다. 실제로 이 문제가 있었다는 것도, 고쳤다는 것도 시험으로 확인했다 — 그룹핑을
다시 문자열 기반으로 되돌려 회귀 시험이 "Expected: 2, Actual: 1"로 실패하는 것까지 확인.

**시험:** `DuplicateFinderWindowConstructionTests.cs`(신규) 3개 — 빈 목록, 평탄화(그룹
2개 → 행 4개), 그리고 위 그룹 합쳐짐 방지. 더블클릭→탐색기 연결 자체는 여전히 손 확인
몫이다(진짜 마우스 라우팅이 필요해 자동 시험으로 흉내내는 게 오히려 부정확하다).

## 4. "순환1 골라도 아무 진행 없음" — 진짜 원인은 `PickOneDialog`의 조용한 취소

**증상(JH 보고, 09-21):** ①을 고친 뒤 다시 확인했는데도 타일 비교가 여전히 아무것도
안 뜬다. 상태 바에 "비교 시작 — ..." 안내조차 안 뜬다("이렇다 할 이벤트조차 없음").

**진단 과정:** 상태 바 문구가 전혀 안 뜬다는 건 `RunCompare` 자체가 안 불렸다는 뜻이라
①(화면에 안 그림) 문제와는 다른, 더 앞단의 결함이다. 순환 타일과의 비교가 뷰모델
단에서 실제로 되는지부터 실측(`TileCompareTests.
ComparingAPinnedTileAgainstARotatingTileAlsoAppliesBadges`)했다 — 되더라(단, 이 시험을
쓰면서 `File.WriteAllText` 두 번을 그냥 순서대로 부르면 두 파일의 `ModifiedUtc`가 실제로
갈릴 수 있어서 시험이 그 자체로 한 번 깨졌다 — `File.SetLastWriteTimeUtc`로 못박아 고침,
이 세션에서 두 번째로 "시험이 실물이 아니라 자기 타이밍 문제를 잰" 경우다). 그래서
순환 타일은 변수가 아니었다.

**진짜 원인:** `PickOneDialog`(상대 타일 고르는 창)의 `ListBox`가 **아무 항목도 기본
선택 없이**(`SelectedIndex == -1`) 뜬다. `Loaded` 핸들러는 키보드 포커스만 옮기고 항목을
고르지 않는다. 목록을 직접 클릭해 하나를 고르지 않은 채 「고르기」를 누르면(또는
`AcceptButton.IsDefault="True"`라 그냥 Enter만 눌러도) `OptionList.SelectedIndex`가
여전히 -1이라 `PickedIndex`가 `null`이 되고, `DialogResult`가 `false`로 닫힌다.
`MainViewModel.OnCompareRequested`는 `picked is null`이면 경고 한 줄 없이 조용히
돌아간다 — 검색 타일·이미 비교 중·후보 없음 세 갈래는 다 `Warn`을 남기는데 이 갈래만
말이 없었다. 후보가 하나뿐인 흔한 상황(타일 두 개만 열려 있을 때)에서 특히 이 모양으로
나타난다 — 유일한 후보인데도 "이미 골라진 것"처럼 보이지 않아서 그냥 확인을 눌렀을
가능성이 높다.

**고침 둘:**
1. `PickOneDialog` 생성자에서 옵션이 있으면 첫 항목을 기본 선택(`SelectedIndex = 0`)해
   둔다 — 흔한 "후보 하나" 상황에서는 그대로 확정되고, 여럿이면 그냥 보이는 기본값이라
   언제든 다시 골라 바꿀 수 있다.
2. `OnCompareRequested`에서 취소(`picked is null`) 갈래에도 `Inform("비교를 취소했다")`를
   남긴다(advisor 지적) — 이러면 다음에 "아무 일도 없다" 보고를 받아도 상태 바만 보고
   코드가 탔는지 안 탔는지 바로 갈릴 수 있다.

**시험:** `PickOneDialogConstructionTests.cs`(신규) — 옵션이 있으면 `SelectedIndex == 0`
인지, 옵션이 없으면 -1인 채로 안 죽는지. 고치기 전 코드로는 이 시험이 "Expected: 0,
Actual: -1"로 실패하는 것까지 확인.

## 5. `[같음]` 배지 추가 — "같은 파일도 표시는 해줘야지" (JH 요청, 09-21)

이전 설계는 "같다(Same)"·"비교 안 걸림(Unset)" 둘 다 아무것도 안 그렸다 — "표시할 것
없음"이라는 논리였는데, JH 지적대로 이러면 **화면만 보고는 "이 항목이 비교 대상이었는데
같아서 조용한 것"과 "그냥 비교 밖인 것"을 구분할 수 없다.** `Unset`은 그대로 안 그리고
(비교와 무관하다는 뜻), `Same`에는 옅은 무채색 `[같음]` 배지를 새로 추가했다 —
`[여기만]`/`[다름]`처럼 굵게 튀지 않게(`FontWeight="Normal"`, `Brush.TextSecondary`) 해서
"확인은 끝났고 문제는 없다"는 낮은 우선순위로 읽히게 했다.

**advisor 주의사항(아직 손으로만 확인 가능):** 두 타일이 대부분 같은 파일이면 이제
거의 모든 줄에 `[같음]`이 붙는다 — `[여기만]`/`[다름]`(실제로 봐야 할 것)이 그 사이에서
덜 튈 수 있다. 색·굵기로 낮춰 뒀지만 실제로 잘 갈리는지는 화면으로만 판단할 수 있다.

## 6. "비교 시작은 뜨는데 배지가 하나도 안 보임" — 창 활성화 재조회와의 경쟁 (확인됨)

**결과(JH 재확인, 09-21): "이번엔 잘 뜸. 비교 폴더와 대상폴더 둘 다 잘뜸."** 아래
가설이 맞았다 — `PickOneDialog`가 닫히며 거는 창 활성화가 `ShowDialog()`의 중첩 모달
루프 안에서(반환 전에) 처리됐다는 뜻이다. 추가로 물었던 "비교 중 alt-tab 해도 유지되는가"
질문은 아직 답을 못 받았다 — 다음 손 확인 때 같이 물을 것.

**증상(JH 보고, 09-21, 4번 고친 뒤에도 재현):** 상대 타일을 고르면 상태 바에 "비교
시작 — ..." 은 뜨는데 그 뒤로 배지가 하나도 안 보인다(회색 `[같음]`조차 없음).

**가설:** `MainWindow.OnActivated`가 "창 활성화 시 재조회"(§8)를 건다 —
`PickOneDialog`가 닫히면서 주 창이 다시 활성화되는 것도 이 재조회를 부른다.
`RunCompare`가 방금 걸어 둔 비교(`ComparePeerA`/`B` + `LoadCompleted` 구독)를, 이
재조회가 완료되며 쏘는 `LoadCompleted`가 **곧바로 `ClearCompare()`로 지워버린다** —
파일 조작 확인·결과 대화상자에 이미 걸려 있던 것과 정확히 같은 종류의 경쟁이다
(`MainViewModel.IsOperationRunning`, `MainWindow.xaml.cs:152` 주석 참고 — 그 가드는
이미 있었는데 이번 기능엔 안 걸려 있었다).

**고침:** `MainViewModel.IsShowingComparePicker`를 새로 두고 `PickOneDialog`를 여는
동안(`_prompt.PickOne(...)` 호출을 감싸는 try/finally) `true`로 켠다.
`MainWindow.OnActivated`가 `IsOperationRunning`과 함께 이 값도 보고 켜져 있으면
재조회를 건너뛴다.

**이 수정이 실제로 통했는지는 아직 모른다 — 손 확인이 결정한다.** 이 가드가 통하려면
`OnActivated`의 동기 검사가 `IsShowingComparePicker`가 아직 `true`인 시점에(=아직
`ShowDialog()` 안에서 빠져나오기 전에) 걸려야 한다. `PickOneDialog`가 닫히며 주 창을
재활성화하는 Win32 `WM_ACTIVATE`가 `ShowDialog()`의 중첩 모달 루프 **안에서** 처리되는지,
아니면 `ShowDialog()`가 반환한 **뒤에** 큐에서 처리되는지는 코드만 보고 확정할 수 없다
(전자면 이 가드로 충분하고, 후자면 플래그가 이미 `false`로 돌아온 뒤라 재조회가 여전히
걸린다 — 그때는 "픽커가 떠 있는 동안"이 아니라 "픽커가 닫힌 뒤 재조회 한 번 건너뛰기"로
고쳐야 한다). **JH의 다음 손 확인이 곧 검증이다** — 배지가 이제 남아 있으면 전자였던
것이고, 여전히 안 보이면 플래그가 너무 일찍 꺼진 것이다.

**더 근본적인 질문(JH가 정할 것):** 이 가설이 맞다면, `PickOneDialog`를 닫을 때만
문제가 아니라 — **비교 중에 다른 창을 봤다가(예: alt-tab) 이 앱으로 돌아오기만 해도**
같은 "창 활성화 시 재조회"가 걸려 비교가 지워진다. 이건 경쟁이 아니라 지금 설계
그대로의 동작이다(`LoadCompleted`→`ClearCompare()`는 무조건이다). 이번 수정은
`PickOneDialog` 닫힘이라는 한 가지 경로만 막았다 — "타일 비교는 창을 벗어났다 와도
유지돼야 하는가"는 별개로 정해야 한다. 지금 구조로는 "창 활성화 시 재조회"와 "비교는
새로 읽으면 무조건 지운다"가 서로 충돌한다 — 둘 중 하나는 양보해야 한다.

**시험:** `TileCompareTests.IsShowingComparePickerIsTrueOnlyWhileThePickerDialogWouldBeOpen`
(신규) — `IsShowingComparePicker`가 `PickOne` 호출 동안만 `true`이고 끝나면 `false`로
돌아오는지 못박는다(가드 없이 되돌리면 이 시험이 실패하는 것도 확인). **실제 Win32
활성화 타이밍 자체는 자동 시험이 원천적으로 못 재현한다** — 이건 위 가설을 검증하는
게 아니라, 가드의 전제(플래그가 올바르게 켜지고 꺼지는가)만 못박는다.

## 7. 내용 비교 — "노란색으로만 표시되면 뭐가 다른지 확인이 안 된다" (JH 요청, 09-21)

**증상:** ⑥이 고쳐진 뒤 이어서 확인해본 "내용 비교"(우클릭 → 나란히 diff 창)는 잘
뜨는데, 바뀐 줄이 통째로 노랗게만 칠해져서 **줄 안에서 정확히 어디가 다른지**는 여전히
눈으로 다시 찾아야 했다.

**고침:** DiffPlex의 단어 단위 하위 diff(`DiffPiece.SubPieces`)를 그동안 안 쓰고
있었다 — `FileContentDiffer.cs` 설계 당시 "V1 화면이 안 쓴다"고 적어 뒀던 그 부분이다.
이제 `ContentDiffLine`에 `Spans`(새 타입 `ContentDiffSpan(Text, IsChanged)`)를 추가해
꺼내고, `ContentDiffWindow`는 **바뀐(Modified) 줄만** 토막 단위로 나눠서 실제로 바뀐
단어만 노랗게 칠한다(안 바뀐 부분은 투명). 완전히 지워지거나(Deleted)/생긴(Inserted)
줄은 여전히 줄 전체를 칠한다 — 그 둘은 줄 전체가 그 뜻이라 나눌 이유가 없다.

**화면 배선:** 줄 하나를 한 `TextBlock`으로 그리던 것을, 토막마다 하나씩
`TextBlock`(배경만 다름)을 가로로 늘어놓는 `ItemsControl`로 바꿨다 — 각 토막을 개별
색으로 칠하려면 이 방법이 제일 XAML 바인딩만으로(코드 비하인드에서 `Run` 조립 없이)
되는 길이다.

**advisor가 남긴 손 확인 항목(자동 시험이 못 보는 것):**
- 시험은 "토막을 순서대로 이으면 원래 줄 문자열 그대로 나온다"까지는 못박았지만, 그건
  **문자열** 얘기다 — 실제 **렌더링**에서 단어 사이 공백·들여쓰기가 그대로 보이는지는
  화면으로만 확인된다(각 토막이 독립된 `TextBlock`이라 공백 처리가 미묘하게 달라질
  가능성이 있다).
- 줄이 길 때 가로 스크롤이 여전히 잘 되는지(토막마다 별도 `TextBlock`이 된 뒤에도).

**시험:** `FileContentDifferTests`(Core)에 3개 추가 —
`ModifiedLineCarriesWordLevelSpansPinpointingTheChange`(바뀐 단어가 `IsChanged=true`로
잡히는지) · `ConcatenatingSpansReconstructsTheOriginalLineText`(토막을 이으면 원문
그대로인지 — 이게 핵심 불변) · `NonModifiedLinesHaveNoSpans`.
`ContentDiffWindowConstructionTests.cs`(신규, App) 3개 — 그동안 `ContentDiffWindow`도
생성 자체를 시험한 적이 없었다(`BatchRenameDialog` 등과 같은 사각지대). 그중
`ModifiedLineProducesMultipleSpansNotOneWholeLineSpan`이 이번 변경의 핵심을 못박는다 —
토막이 하나뿐이면(옛 "줄 전체 뭉개기" 동작으로 되돌아간 것이면) 이 시험이 잡는다.

**아직 안 답 받은 질문(이전 라운드에서 넘어온 것):** 두 타일이 대부분 같은 파일일 때
`[다름]`이 `[같음]` 사이에서 잘 눈에 띄는지 — 6번 확인 때 같이 안 물어봤다, 다음 손
확인에 포함.

## 부수 정리 — 시험 인프라

`BatchRenameDialogConstructionTests`·`DuplicateFinderWindowConstructionTests`·
`PickOneDialogConstructionTests` 셋 다 실제 WPF `Window`를 STA 스레드에서 생성해봐야
하고, XAML이 `{StaticResource Brush.*}`를 써서 `Application.Current`(테마 리소스 병합)가
있어야 한다. 처음엔 파일마다 이 설정을 복붙했는데, xUnit이 서로 다른 테스트 클래스를
기본적으로 **병렬로** 돌리는 바람에 두 클래스가 동시에 `Application`을 만들려다 죽을 수
있는 경쟁 상태였다(advisor 지적, 실행 순서가 우연히 안 겹쳐서 처음엔 안 죽었을 뿐).
`StaWpfTestSupport.cs`로 하나로 모아 `lock`도 하나만 쓰게 고쳤다.
`PanelFocusPolicyTests`·`VirtualizingWrapPanelLayoutTests`는 `Application`을 안 건드리는
맨 컨트롤만 써서 이 헬퍼가 필요 없다 — 일부러 그대로 뒀다.

## 게이트

- 시험: Core 341→344(+3, `FileContentDifferTests`의 스팬 시험), App 937→950(+13) —
  `BatchRenameDialogConstructionTests` 2 · `DuplicateFinderWindowConstructionTests` 3 ·
  `TileCompareTests` 신규 2(순환 타일 비교, `IsShowingComparePicker` 토글) ·
  `PickOneDialogConstructionTests` 3 · `ContentDiffWindowConstructionTests` 3.
  `StaWpfTestSupport.cs`는 헬퍼만 담고 `[Fact]`가 없어 개수에 안 잡힌다. 전부 통과,
  회귀 0.
- 빌드: 0 오류 · 0 경고.
- git 상태: `git status --porcelain -- src tests doc` — 수정 14개(MainWindow.xaml.cs,
  MainViewModel.cs, BatchRenameDialog.xaml·.xaml.cs, ContentDiffWindow.xaml·.xaml.cs,
  DuplicateFinderWindow.xaml·.xaml.cs, FolderPanelView.xaml, PickOneDialog.xaml.cs,
  FileContentDiffer.cs, LauncherFixture.cs, TileCompareTests.cs, FileContentDifferTests.cs)
  + 신규 6개(시험 파일 다섯 + 이 보고서). **커밋 안 됨** — 이번 라운드는 별도 요청이
  필요하다.
- **JH가 다시 손 확인할 것 — 우선순위 순:**
  1. 내용 비교 창에서 이제 바뀐 단어만 노랗게 칠해지는지, 긴 줄에서 공백·들여쓰기가
     그대로 보이는지(⑦).
  2. 비교 중에 다른 창으로 갔다가(alt-tab) 이 앱으로 돌아오면 비교가 그대로 있는지,
     아니면 사라지는지 — 사라진다면 그게 지금 설계 그대로인지 원하는 동작인지 정해야
     한다(⑥ "더 근본적인 질문" 참고, 아직 안 물어봄).
  3. 두 타일이 대부분 같은 파일일 때도 `[다름]`이 `[같음]` 사이에서 잘 눈에 띄는지(아직
     안 물어봄).
  4. Ctrl+F2가 이제 안 터지는지(단일/다중 선택 둘 다) · 새 표 형태 중복 찾기가 읽기
     편한지 — 이 둘은 이미 "된다" 확인받음, 재확인 우선순위 낮음.
