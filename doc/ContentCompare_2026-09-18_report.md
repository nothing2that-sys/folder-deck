# 내용 비교 — ①의 연장 (2026-09-18)

`doc/FeatureCandidates_2026-09-18_implementation_report.md`(커밋 `7db4b64`)는 이미
커밋됐고 그 규약(보고문 단정은 증거를 넘지 않는다 · 커밋된 보고문은 고치지 않는다)에
따라 손대지 않는다 — 이 문서는 같은 날 이어진 별도 라운드다. ①(타일 간 비교)이 이름·
크기·수정일까지만 보고 끝내던 것을, "다름"으로 표시된 파일 한 쌍의 **실제 내용**까지
보여주는 데까지 확장했다.

## 무엇을 만들었나

우클릭 → 「내용 비교」(비교 중이고 그 행이 `[다름]`일 때만 뜬다) → 나란히(사이드바이
사이드) 2단 diff 창.

- **diff 계산**: `src/FolderDeck.Core/Comparison/FileContentDiffer.cs` — 순수 함수,
  DiffPlex(Apache License 2.0, NuGet, 의존성 0개) 감싼다. DiffPlex 타입은 밖으로
  안 새고 `ContentDiffLine`/`ContentDiffLineKind`로만 나간다. 7개 시험.
- **파일 읽기**: `src/FolderDeck.App/Services/FileContentReader.cs` — 이진 판정(NUL
  바이트 휴리스틱) · 5MB 크기 상한 · 인코딩 추정(BOM → UTF-8 엄격 → 실패 시 CP949).
  10개 시험.
- **창**: `src/FolderDeck.App/Views/ContentDiffWindow.xaml`(+`.xaml.cs`) — 비모달,
  줄 단위로 색칠(삭제=`Brush.DangerSurface`, 추가=신설 `Brush.SuccessSurface`, 변경=
  신설 `Brush.WarningSurface`).
- **진입점**: 셸 컨텍스트 메뉴에 항목 하나 추가(`ShellMenuPolicy.CompareContent`,
  `id=0x16`) — 앱 항목 목록 뒤에 셸 항목이 그대로 이어지는 기존 구조(§4.6·D34)에
  `canCompareContent` 조건 하나만 얹었다.
- **연결**: `FolderPanelViewModel.CompareContentRequested` 델리게이트 →
  `MainViewModel.FindCompareCounterpart`(순수 조회, 5개 시험) →
  `OnCompareContentRequested`(읽기+diff+창 띄우기, 미시험 — 아래 참고).

## 설계 결정과 근거 (대화에만 있던 것 — 코드 주석에 옮겨 적음)

- **나란히(사이드바이사이드) vs 위아래(유니파이드)**: JH 선택. WinMerge 스타일 2단으로
  ①의 배지 방향(왼쪽=우클릭한 타일, 오른쪽=상대 타일)과 시각적으로 일치시켰다.
- **컨텍스트 메뉴 우클릭 vs 독립 "두 파일 비교" 메뉴**: JH 선택. 기존 ① 비교 흐름을
  그대로 타면 새 파일 선택 UI가 필요 없다 — 범위가 훨씬 작다.
- **WinMerge 대신 DiffPlex(NuGet)**: WinMerge 는 Everything(`Everything.dll`)처럼
  써먹을 헤드리스 SDK 가 없다 — 창 전체를 띄우는 것(`Process.Start`) 말고는 못 붙인다.
  DiffPlex 는 순수 관리 코드 라이브러리라 Everything 과 같은 패턴(라이브러리 호출 →
  우리 UI 로 결과 렌더)으로 내부화할 수 있다.
- **CP949 폴백을 추가한 이유**: 이 앱은 한국어 환경 전용이다. UTF-8만 가정하면 CP949로
  저장된 레거시 텍스트 파일(드물지 않다)이 전부 깨진 문자로 보인다 — 기능이 있으나마나
  해진다. 이건 범위 확장이 아니라 타깃 로케일에서의 기본 정확성으로 봤다.
- **Apache 2.0 고지**: 기존 `src/FolderDeck.App/Native/THIRD-PARTY-NOTICES.txt`(빌드·
  게시 출력에 그대로 복사되는 유일한 고지 파일, Everything SDK 고지가 이미 있던 자리)에
  DiffPlex 전문을 이어 붙였다 — 새 고지 파일을 안 만들었다.

## advisor 라운드에서 고친 것

- `Interop/ShellMenuPolicy.cs`: `Ctrl+D`/`Ctrl+F2` 헤더 아이콘 주석의 잘못된 근거
  ("새 아이콘이 290px 계산에 영향을 준다")를 고쳤다 — 실제 이유는 그 폭에서 항상 보이는
  아이콘 열이든 "⋯" 메뉴든 항상 자리를 하나 늘리는 것.
- `tests/FolderDeck.App.Tests/ShellMenuPolicyTests.cs`의 `UnknownAppBandIdRunsNothing`
  이 "안 쓰는 id" 예시로 `0x16`을 썼었는데, 그 값이 이번에 `CompareContent`로 배정되며
  전제가 깨졌다 — 기대값을 바꾸지 않고 예시 id를 `0x17`로 옮겼다(진짜 안 쓰는 값으로).
- `FileContentReader.DecodeText`의 문서 주석이 "CP949도 안 되면 `DecoderFallbackException`
  이 그대로 올라간다"고 **검증 없이** 적혀 있었다 — 실측해보니 틀렸다.
  `Encoding.GetEncoding(949)`는 기본으로 예외 폴백이 아니라 치환 폴백을 쓴다(무효한
  바이트는 `?`로 조용히 바뀐다, 절대 안 던진다). 주석을 실측대로 고치고
  `NeitherUtf8NorCp949BytesDecodeWithoutThrowingInsteadOfCrashing` 시험으로 못박았다 —
  UTF-8도 CP949도 아닌 인코딩(Shift-JIS·Latin-1 등)의 파일은 예외가 아니라 조용히 깨진
  문자로 보인다, 이건 V1이 받아들인 한계다.
- `System.Text.Encoding.CodePages` 패키지에 대한 NU1510("안 써도 될 것 같다") 경고를
  `NoWarn`으로 껐다 — `CodePagesEncodingProvider` 타입 자체가 이 패키지에만 있어 실제로는
  필수다.

## 시험이 못 보는 것 (손 확인 필요)

- `ContentDiffWindow`의 실제 렌더링 — 줄 색깔이 의도대로 보이는지, 긴 줄에서 가로 스크롤이
  동작하는지, 왼쪽/오른쪽 헤더에 타일 이름이 맞게 뜨는지.
- 셸 컨텍스트 메뉴에 「내용 비교」가 **`[다름]` 행에서만** 실제로 나타나는지 — 이건 COM
  interop(`IContextMenu2/3`)이라 자동 시험이 아예 못 닿는다(기존 `ShellMenuPolicyTests`
  주석이 이미 명시한 한계와 같다).
- CP949 폴백이 실제 한국어 레거시 텍스트 파일(메모장에서 "ANSI"로 저장한 것 등)에서
  기대대로 동작하는지 — 시험은 인코딩 바이트를 직접 만들어 확인했지, 실물 파일로는 아직
  안 재봤다.

## 게이트

- 시험: Core 341/341(+7), App 937/937(+20). 전부 통과, 회귀 0.
- 빌드: 0 오류 · 0 경고(NU1510 억제 포함).
- git 상태: `git status --porcelain -- src tests doc` — 수정 10개 + 신규 7개(이 보고서
  포함). **커밋 안 됨** — 이번 라운드는 아직 "커밋해줘" 요청을 못 받았다(직전 라운드에
  받은 요청은 그 라운드 몫에만 해당한다).
