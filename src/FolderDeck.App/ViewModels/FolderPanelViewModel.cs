using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Paths;
using IoDirectory = System.IO.Directory;
using IoPath = System.IO.Path;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 폴더 패널 하나 — 상시 칸이든 순환 칸이든 같은 컨트롤을 쓴다 (§3.2).
/// </summary>
/// <remarks>
/// 앵커(<see cref="FolderEntry.Path"/>)와 <see cref="CurrentPath"/> 는 분리돼 있다(§4.6).
/// 하위 진입·상위 이동은 <see cref="CurrentPath"/> 만 바꾸며 <b>저장하지 않는다</b> —
/// 그래서 앱을 다시 열면 앵커로 리셋된다. 탐색 히스토리와 검색어도 같이 런타임 전용이다.
/// </remarks>
public sealed partial class FolderPanelViewModel : ObservableObject
{
    /// <summary>재귀 검색 결과를 UI 에 밀어 넣는 묶음 크기 / 간격.</summary>
    private const int SearchFlushBatch = 32;
    private const int SearchFlushMs = 150;

    /// <summary>검색어를 타이핑하는 동안 트리를 반복해서 걷지 않도록 두는 여유.</summary>
    private const int RecursiveSearchDebounceMs = 250;

    /// <summary>
    /// 전역 검색 타일(D69-e)이 타이핑마다 Everything 을 부르지 않도록 두는 여유. Everything
    /// 색인 질의 자체는 보통 즉시 끝나지만, 서비스가 단일 전용 스레드로 질의를 직렬화한다
    /// (D69-b) — 디바운스가 없으면 빠르게 치는 글자 수만큼 그 스레드에 작업이 쌓인다.
    /// <see cref="RecursiveSearchDebounceMs"/> 와 같은 값을 골랐다 — 이 파일 안에 이미 검증된
    /// 여유이고, 굳이 다른 값을 새로 정할 근거가 없었다(보고문 §5).
    /// </summary>
    private const int EverythingSearchDebounceMs = 250;

    private static readonly char[] Separators =
        [IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar];

    private readonly IFolderEnumerator _enumerator;
    private readonly IShellLauncher _shell;
    private readonly IClipboardService _clipboard;
    private readonly Action<string>? _reportError;

    /// <summary>
    /// 거절을 알리는 통로. 실패(<see cref="_reportError"/>)와 결이 달라 따로 받는다 —
    /// 거절은 스스로 사라지고 실패는 남는다 (§5.1).
    /// </summary>
    private readonly Action<string>? _reportRejection;

    /// <summary>진행 중인 열거. 폴더를 빠르게 갈아타면 이전 것을 취소한다.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>진행 중인 검색. 열거와 별개로 취소된다.</summary>
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// 방문 경로 히스토리 — 브라우저와 같은 감각. 런타임 전용이라 저장하지 않는다.
    /// 경로만이 아니라 <see cref="FolderEntry"/> 까지 담는다: 왼쪽 리스트로 다른 폴더를
    /// 띄우는 것도 히스토리에 쌓이므로, 뒤로 가면 그때의 앵커·뷰·정렬까지 돌아와야 한다.
    /// </summary>
    private readonly Stack<HistoryPosition> _back = new();
    private readonly Stack<HistoryPosition> _forward = new();

    public FolderPanelViewModel(
        IFolderEnumerator enumerator,
        IShellLauncher shell,
        IClipboardService clipboard,
        bool isRotating,
        bool isSearchTile = false,
        Action<string>? reportError = null,
        Action<string>? reportRejection = null)
    {
        _enumerator = enumerator;
        _shell = shell;
        _clipboard = clipboard;
        _reportError = reportError;
        _reportRejection = reportRejection;
        this.isRotating = isRotating;
        this.isSearchTile = isSearchTile;

        // 검색 줄이 늘 펴져 있다(D69-e 6-2) — 접거나 펴는 손잡이 자체가 이 타일엔 없다.
        if (isSearchTile)
        {
            isSearchExpanded = true;
        }
    }

    private readonly record struct HistoryPosition(FolderEntry Entry, string Path);

    /// <summary>
    /// 순환 칸이면 true(점선), 상시 칸이면 false(실선). §2.3
    /// <para>배치 편집에서 칸 성격을 토글하면 바뀌므로 읽기 전용이 아니다(Phase 2).</para>
    /// </summary>
    [ObservableProperty]
    private bool isRotating;

    /// <summary>
    /// 확장 중인가(Phase 19 · D116) — <see cref="TileViewModel.IsExpanded"/> 의 사본이다.
    /// 헤더의 확장 아이콘 글리프·툴팁을 고르는 데만 쓴다 — 배치는 <c>TileGridView</c> 가
    /// <c>TileViewModel</c> 을 직접 보고 정한다(§4.4).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    [NotifyPropertyChangedFor(nameof(ExpandTip))]
    private bool isExpanded;

    /// <summary>확장 ⤢ · 복원 ⤡ 토글(§4-7 (b)) — <see cref="ViewModeIcon"/> 과 같은 모양이다.</summary>
    public string ExpandIcon => IsExpanded ? "⤡" : "⤢";

    public string ExpandTip => IsExpanded ? "원래 크기로 복원" : "타일을 화면 전체로 확장";

    /// <summary>
    /// 확장 버튼이 누른 커맨드가 닿을 통로(Phase 19 · D116) — <see cref="MainViewModel"/> 의
    /// <c>ExpandedTile</c> 을 토글한다. <see cref="RegisterFolderCommand"/> 와 같은 결이다 —
    /// 패널은 자기가 어느 타일인지도, 격자 전체 상태도 모른다.
    /// </summary>
    public System.Windows.Input.ICommand? ToggleExpandCommand { get; set; }

    /// <summary>
    /// 전역 검색 타일인가(D69-e 결정 2·3). <c>Entry</c>·<c>CurrentPath</c> 가 늘 null 인
    /// 자리라 <see cref="IsEmpty"/> 하나만 새로 가른다 — 나머지(<see cref="CanGoUp"/> 등)는
    /// 이미 null 로 막혀 있다. 생성자로만 정해지고 타일의 남은 수명 동안 바뀌지 않는다
    /// (<see cref="IsRotating"/> 과 같은 자리).
    /// </summary>
    [ObservableProperty]
    private bool isSearchTile;

    /// <summary>
    /// 빈 칸에 뜨는 제목. 순환 칸이 여럿이면 어느 번호인지 여기서 말한다
    /// (예: "순환2 — 폴더를 지정하세요").
    /// </summary>
    [ObservableProperty]
    private string emptyTitle = "폴더를 지정하세요";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(AnchorName))]
    [NotifyPropertyChangedFor(nameof(MetaText))]
    [NotifyPropertyChangedFor(nameof(MetaCountText))]
    [NotifyPropertyChangedFor(nameof(ItemCountText))]
    [NotifyPropertyChangedFor(nameof(ViewModeText))]
    [NotifyPropertyChangedFor(nameof(ViewModeIcon))]
    [NotifyPropertyChangedFor(nameof(ViewModeTip))]
    [NotifyPropertyChangedFor(nameof(SortIcon))]
    [NotifyPropertyChangedFor(nameof(SortTip))]
    [NotifyPropertyChangedFor(nameof(HasStatusLine))]
    [NotifyPropertyChangedFor(nameof(MetaSortText))]
    [NotifyPropertyChangedFor(nameof(FoldersFirst))]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    [NotifyPropertyChangedFor(nameof(SortArrow))]
    // 컬럼 머리글도 Entry 를 읽는다 (Phase 8-B ①) — 순환 칸이 다른 폴더로 갈아탈 때
    // 화살표가 옛 폴더의 정렬 컬럼에 남지 않게 한다.
    [NotifyPropertyChangedFor(nameof(NameHeaderText))]
    [NotifyPropertyChangedFor(nameof(SizeHeaderText))]
    [NotifyPropertyChangedFor(nameof(ModifiedHeaderText))]
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsLargeIconView))]
    [NotifyPropertyChangedFor(nameof(IsExtraLargeIconView))]
    [NotifyPropertyChangedFor(nameof(CurrentShowPreview))]
    [NotifyPropertyChangedFor(nameof(CurrentPreviewRatio))]
    private FolderEntry? entry;

    /// <summary>지금 보고 있는 경로. <b>런타임 전용 — JSON 에 저장하지 않는다</b>(§4.6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAwayFromAnchor))]
    [NotifyPropertyChangedFor(nameof(ShowsAwayFromAnchor))]
    [NotifyPropertyChangedFor(nameof(HasStatusLine))]
    [NotifyPropertyChangedFor(nameof(LocationHint))]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    private string? currentPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusLine))]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    [NotifyPropertyChangedFor(nameof(FailureText))]
    private FolderAccessFailure? failure;

    /// <summary>
    /// 확장자→글리프 사용자 매핑(D59). 패널은 저장소를 모른다 — <see cref="MainViewModel"/> 이
    /// 타일을 만들 때 물려주고, 설정 창에서 바뀌면 다시 물려준 뒤 <see cref="RefreshAsync"/> 로
    /// 다시 읽어 새 글리프가 반영된다. 비어 있으면 지금과 같다(결정 6).
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtensionGlyphs { get; set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// 파일 목록 아이콘 3단(§3.2) 중 <b>② 단</b>(셸 아이콘) 취득 계층(Phase 11 · D68-c).
    /// <see cref="ExtensionGlyphs"/> 와 같은 길로 들어온다 — 패널은 만들지 않고,
    /// <see cref="MainViewModel"/> 이 앱 전체에 하나 만들어 물려준다. <c>null</c> 이면
    /// <see cref="FileItemViewModel"/> 은 셸을 부르지 않고 ③ 단(글리프)만 쓴다.
    /// </summary>
    public ShellIconService? ShellIconService { get; set; }

    /// <summary>
    /// 전역 검색 타일(§4.12)의 질의 계층. <see cref="ExtensionGlyphs"/>·<see cref="ShellIconService"/>
    /// 와 같은 길이다 — 패널은 만들지 않고 <see cref="MainViewModel"/> 이 앱 전체에 하나 만들어
    /// 물려준다(D69-e 결정 5·6-4). 검색 타일이 아니면 늘 <c>null</c> 이다.
    /// </summary>
    public EverythingSearchService? EverythingSearchService { get; set; }

    /// <summary>
    /// 전역 검색 한 번의 결과 상한. 타일을 만들 때 <c>settings.EverythingMaxResults</c> 의
    /// 스냅숏으로 받는다(D69-e 결정 5) — 설정 창에서 값이 바뀌면 <see cref="MainViewModel"/> 이
    /// 떠 있는 검색 타일에 다시 물려준다.
    /// </summary>
    public int EverythingMaxResults { get; set; } = AppSettings.DefaultEverythingMaxResults;

    /// <summary>
    /// 전역 검색이 정상이 아닐 때의 안내 문구(D69-e 결정 7) — <see cref="EverythingStatus.Describe"/>
    /// 와 같은 말을 쓴다. 검색이 정상이거나 검색 타일이 아니면 <c>null</c> 이다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(HasEverythingStatusText))]
    private string? everythingStatusText;

    public bool HasEverythingStatusText => EverythingStatusText is not null;

    /// <summary>전역 검색 결과가 상한에 잘렸는가(D69-e 결정 6) — 잘렸으면 개수 문구가 총계도 적는다.</summary>
    private bool everythingTruncated;

    /// <summary>전역 검색이 실제로 찾은 전체 개수 — <see cref="everythingTruncated"/> 일 때만 뜻이 있다.</summary>
    private int everythingTotalCount;

    /// <summary>
    /// 검색 타일이 스스로 드는 정렬 기준(D69-f 결정 2) — <c>Entry</c> 가 없어 폴더 타일처럼
    /// <c>FolderEntry.SortBy</c>/<c>SortDesc</c> 에 저장할 자리가 없다. 저장하지 않는다 —
    /// 질의 문자열을 저장하지 않는 것과 같은 이유다. 기본값은 이름 오름차순(D69-e 가 고정으로
    /// 쓰던 값 그대로).
    /// </summary>
    private SortBy searchSortBy = SortBy.Name;

    private bool searchSortDesc;

    /// <summary>지금 정렬 기준 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    private SortBy CurrentSortBy => IsSearchTile ? searchSortBy : Entry?.SortBy ?? SortBy.Name;

    /// <summary>지금 정렬 방향 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    private bool CurrentSortDesc => IsSearchTile ? searchSortDesc : Entry?.SortDesc == true;

    /// <summary>
    /// 검색 타일이 스스로 드는 컬럼 표시 상태(Phase 15 · D79) — <c>Entry</c> 가 없어 폴더
    /// 타일처럼 <c>FolderEntry.ShowSize</c>/<c>ShowModified</c> 에 저장할 자리가 없다.
    /// 저장하지 않는다 — <see cref="searchSortBy"/> 를 저장하지 않는 것과 같은 이유다.
    /// 셋 다 초기값은 「보인다」.
    /// </summary>
    private bool searchShowPosition = true;

    private bool searchShowSize = true;

    private bool searchShowModified = true;

    /// <summary>
    /// 검색 타일이 스스로 드는 미리보기 칸 상태(Phase 17 · D86 · §7.1 v9) — <c>Entry</c> 가
    /// 없어 폴더 타일처럼 <c>FolderEntry.ShowPreview</c> 에 저장할 자리가 없다. 저장하지
    /// 않는다 — 위 셋과 같은 이유다. <b>초기값이 위 셋과 반대인 것이 요점이다</b> — 미리보기는
    /// 켜짐이 기본이 아니라 꺼짐이 기본이다(Phase 17 전에는 없던 기능이라서).
    /// </summary>
    private bool searchShowPreview;

    private double searchPreviewRatio = FolderEntry.DefaultPreviewRatio;

    /// <summary>지금 `크기` 열 표시 여부 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    /// <remarks>
    /// <b>공개다</b>(D79-b-fix F-1) — <see cref="CurrentSortBy"/> 와 달리 이 값 자체를 D79-c 가
    /// 컬럼 메뉴의 체크 상태에 바깥에서 묶어야 한다. WPF 바인딩·다른 클래스의 코드비하인드는
    /// 공개 멤버만 본다.
    /// </remarks>
    public bool CurrentShowSize => IsSearchTile ? searchShowSize : Entry?.ShowSize ?? true;

    /// <summary>지금 `수정` 열 표시 여부 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    /// <remarks><see cref="CurrentShowSize"/> 와 같은 이유로 공개다.</remarks>
    public bool CurrentShowModified => IsSearchTile ? searchShowModified : Entry?.ShowModified ?? true;

    /// <summary>지금 미리보기 칸 표시 여부 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    /// <remarks>
    /// <see cref="CurrentShowSize"/> 와 같은 이유로 공개다. <b>폴백이 <c>?? false</c> 인 것이
    /// 형제들과 다른 점이다</b> — <c>Entry</c> 가 아직 없는 빈 순환 칸에서는(§2.3) 「꺼짐」이
    /// 이 칸의 기본값이므로, 형제들의 <c>?? true</c> 를 그대로 따라가면 안 켠 기능이 켜진
    /// 것처럼 보인다.
    /// </remarks>
    public bool CurrentShowPreview => IsSearchTile ? searchShowPreview : Entry?.ShowPreview ?? false;

    /// <summary>지금 미리보기 칸 비율 — 검색 타일은 자체 상태, 폴더 타일은 <see cref="Entry"/> 것.</summary>
    /// <remarks><see cref="CurrentShowPreview"/> 와 같은 이유로 공개다.</remarks>
    public double CurrentPreviewRatio =>
        IsSearchTile ? searchPreviewRatio : Entry?.PreviewRatio ?? FolderEntry.DefaultPreviewRatio;

    /// <summary>
    /// 지금 `위치` 열 표시 여부 — <b>검색 타일에만 있는 개념이라 <see cref="Entry"/> 쪽 짝이
    /// 없다</b>(D78 B-0 §4). <see cref="CurrentSortBy"/> 처럼 타일 종류로 가르지 않는다 —
    /// 가를 대상 자체가 없어서, 물으면 항상 검색 상태를 그대로 준다. 폴더 타일 쪽 진입점은
    /// 이 프로퍼티를 아예 묻지 않는 방식으로 짠다(D79-b §6-3).
    /// </summary>
    /// <remarks><see cref="CurrentShowSize"/> 와 같은 이유로 공개다(D79-b-fix F-1).</remarks>
    public bool CurrentShowPosition => searchShowPosition;

    /// <summary>현재 경로를 열거한 원본 목록. 검색·필터는 이걸 건드리지 않는다.</summary>
    [ObservableProperty]
    private IReadOnlyList<FileItemViewModel> items = [];

    /// <summary>
    /// 실제로 화면에 그리는 목록 — 평소엔 <see cref="Items"/>, 필터 중엔 걸러낸 것,
    /// 재귀 검색 중엔 결과가 쌓이는 컬렉션.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetaText))]
    [NotifyPropertyChangedFor(nameof(MetaCountText))]
    [NotifyPropertyChangedFor(nameof(ItemCountText))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private IReadOnlyList<FileItemViewModel> displayItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationHint))]
    private IReadOnlyList<BreadcrumbSegment> breadcrumbs = [];

    // ── 검색 상태 ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    [NotifyPropertyChangedFor(nameof(IsRecursiveSearch))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(NeedsLocationColumn))]
    private string searchText = string.Empty;

    /// <summary>"하위 포함" 체크. 끄면 즉석 필터, 켜면 재귀 검색.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecursiveSearch))]
    [NotifyPropertyChangedFor(nameof(NeedsLocationColumn))]
    private bool includeSubfolders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private bool isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private int searchScannedFolders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedFolders))]
    [NotifyPropertyChangedFor(nameof(SkippedFoldersText))]
    private int searchSkippedFolders;

    /// <summary>
    /// 검색창이 펴져 있는가. <b>런타임 전용 순간 UI 상태라 저장하지 않는다</b> —
    /// 왼쪽 컬럼 접기(사용자 설정, 저장 대상)와 반대다. 항상 접힌 채로 시작한다.
    /// </summary>
    [ObservableProperty]
    private bool isSearchExpanded;

    /// <summary>
    /// 지금 이 패널에서 고른 항목들 (§4.2 · §4.8). 뷰의 ListView 선택이 그대로 흘러든다.
    /// </summary>
    /// <remarks>
    /// <b>선택은 타일 하나에만 존재한다</b>(§4.8) — 다른 타일을 고르면 이전 타일이 풀린다.
    /// 그 조정은 <see cref="MainViewModel"/> 이 한다. 매크로·대상함의 "현재 선택"이
    /// 어디를 말하는지가 흔들리지 않게 하려는 규칙이다.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    [NotifyPropertyChangedFor(nameof(SelectionSuffix))]
    [NotifyPropertyChangedFor(nameof(PreviewTarget))]
    private IReadOnlyList<FileItemViewModel> selectedItems = [];

    public bool HasSelection => SelectedItems.Count > 0;

    public string? SelectionText => HasSelection ? $"{SelectedItems.Count}개 선택" : null;

    /// <summary>
    /// 개수 뒤에 이어 붙는 선택 조각 — " · 2개 선택" (Phase 8-D ①).
    /// </summary>
    /// <remarks>
    /// <b>구분점을 이 조각이 들고 온다.</b> 앞의 <see cref="ItemCountText"/> 와 한 줄로 붙으므로
    /// 구분점을 XAML 에 두면 선택이 없을 때 점만 뜬 꼬리가 남는다(런처 카드 3줄째와 같은 규칙).
    /// <para>
    /// 이 조각이 생기는 자리는 헤더 <b>1줄</b>이다. 8-C 까지는 2줄째에 있었는데, 그 줄이
    /// 있을 때만 뜨는 줄이라 <b>고르는 순간 헤더가 한 줄 자라고 풀면 다시 줄었다</b> —
    /// 목록을 고르는 손 아래에서 목록이 위아래로 뛰었다. 1줄은 늘 있으므로 글자만 길어진다.
    /// </para>
    /// </remarks>
    public string SelectionSuffix => HasSelection ? $" · {SelectionText}" : string.Empty;

    /// <summary>
    /// 미리보기 칸에 그릴 대상(Phase 17 · D86 · D95 §2 결정 6). 고른 것이 <b>파일 하나</b>일
    /// 때만 값이 있다 — 여럿이면 아무것도 안 보인다(§3.2), 폴더는 대상이 아니다(D95 §2 결정 7,
    /// 셸 썸네일이 나오긴 하나 「미리보기」로 읽히지 않는다는 새 결정이다).
    /// </summary>
    public FileItemViewModel? PreviewTarget =>
        SelectedItems.Count == 1 && !SelectedItems[0].IsDirectory ? SelectedItems[0] : null;

    /// <summary>선택을 푼다. 뷰가 목록에서 실제로 풀도록 이벤트로도 알린다.</summary>
    public void ClearSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        SelectedItems = [];
        SelectionCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>다른 타일이 선택을 가져가서 여기 선택을 풀어야 한다.</summary>
    public event EventHandler? SelectionCleared;

    /// <summary>
    /// 목록을 다시 읽은 뒤 <b>같은 경로를 다시 골라 달라</b>고 뷰에 알린다 (§4.8).
    /// </summary>
    /// <remarks>
    /// <see cref="Items"/> 를 갈아끼우면 <c>ListView</c> 가 자기 선택을 스스로 비우고
    /// 그 통지가 <see cref="SelectedItems"/> 까지 지운다. 그대로 두면 창을 잠깐 떠났다
    /// 돌아오기만 해도(§8 활성화 재조회) 고른 것이 사라진다 — 매크로를 만들고 바로 실행하면
    /// "대상이 없다"가 뜨는 것이 그 증상이었다.
    /// </remarks>
    public event EventHandler? SelectionRestored;

    /// <summary>
    /// 지금 선택을 드래그 뭉치로 만든다 (§4.2-2). 선택이 없으면 null — 끌 것이 없다.
    /// </summary>
    /// <param name="pressedItem">
    /// 드래그를 시작한 마우스 다운이 누른 항목 (D72). 누른 뒤 임계를 넘기기까지 사이에
    /// <see cref="SelectedItems"/> 가 바뀔 수 있어 — 다른 타일을 눌러 선택이 옮겨가거나,
    /// 아무것도 안 골라진 채 파일 하나를 눌러 곧장 끄는 경우 — 그 순간의 선택만 보면
    /// 엉뚱한 것이 끌리거나(D70 Q2) 드래그가 아예 시작되지 않는다.
    /// </param>
    /// <remarks>
    /// 누른 항목이 없으면(<see langword="null"/>) 지금까지와 같다 — 선택 전체, 선택이
    /// 비면 <see langword="null"/>. 누른 항목이 있으면 <b>그것이 선택 안에 있는가</b>로
    /// 갈린다: 안에 있으면(탐색기처럼 여럿 고른 것 중 하나를 끌면 뭉치째 간다) 선택 전체를,
    /// 밖이면(선택이 비어 있는 경우를 포함) 그 항목 하나만 싣는다.
    /// <para>
    /// <b>참조 같음이 아니라 <see cref="FileItemViewModel.FullPath"/> 로 견준다</b>(대소문자
    /// 무시) — 목록을 다시 읽으면 같은 파일도 새 인스턴스로 오므로(1450행 근처의 선택
    /// 복원과 같은 이유·같은 비교자, <see cref="StringComparer.OrdinalIgnoreCase"/>).
    /// </para>
    /// </remarks>
    public FileDropPayload? BuildDragPayload(FileItemViewModel? pressedItem = null)
    {
        // 검색 타일은 CurrentPath 가 늘 null 이다(Entry 가 없다) — 이 가드를 검색 타일에서만
        // 푼다(D69-f 결정 4). 폴더 타일은 지금 그대로 막는다.
        if (!IsSearchTile && CurrentPath is null)
        {
            return null;
        }

        var pressedIsInSelection = pressedItem is not null && SelectedItems.Any(i =>
            string.Equals(i.FullPath, pressedItem.FullPath, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<FileItemViewModel> items = pressedItem is null || pressedIsInSelection
            ? SelectedItems
            : [pressedItem];

        if (items.Count == 0)
        {
            return null;
        }

        // 검색 타일은 결과가 여러 볼륨에서 섞여 와 뭉치의 출처가 하나로 안 정해진다(D69-f) —
        // 끌린 첫 항목의 부모 폴더를 넣는다. 이동/복사 판정은 항목마다 하므로 안 깨진다.
        var sourceFolder = IsSearchTile ? SourceFolderFor(items[0]) : CurrentPath!;

        return new FileDropPayload(
            sourceFolder,
            [.. items.Select(i => new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))]);
    }

    /// <summary>
    /// 검색 타일에서 끌린 뭉치의 출처(D69-f 결정 5) — 끌린 첫 항목의 부모 폴더. 부모를 못
    /// 구하면(드라이브 루트 자체가 끌렸을 때 등) 그 항목의 전체 경로로 대신한다. 이 값은
    /// 문구·진단에만 쓰이고 실제 파일 조작에는 안 쓰인다(§6-1 감사) — 그래서 이 대체가
    /// 안전하다.
    /// </summary>
    private static string SourceFolderFor(FileItemViewModel item) =>
        IoPath.GetDirectoryName(item.FullPath) is { Length: > 0 } parent ? parent : item.FullPath;

    /// <summary>
    /// 우클릭 메뉴 "휴지통으로 삭제"가 닿을 통로 (§4.4). 실제 명령은 엔진을 아는
    /// <see cref="MainViewModel"/> 것이고, 패널은 실행 방법을 모른다 — 타일을 만들 때 걸어 준다.
    /// </summary>
    /// <remarks>
    /// 키보드 <c>Del</c> 은 창(<c>MainWindow</c>)의 InputBinding 이 직접 받으므로 이 통로가
    /// 필요 없다. 우클릭 메뉴만 패널의 <c>DataContext</c> 안에 갇혀 있어 이 한 칸이 필요하다.
    /// </remarks>
    public System.Windows.Input.ICommand? TrashSelectionCommand { get; set; }

    /// <summary>
    /// 셸에서 끌어온 것을 이 타일에 받는 통로 (§4.9). 패널은 엔진도, 다른 조작이 도는지도
    /// 모른다 — <see cref="TrashSelectionCommand"/> 와 같이 타일을 만들 때 걸어 준다.
    /// </summary>
    public IShellDropHost? ShellDropHost { get; set; }

    /// <summary>
    /// 밖으로 넘긴 뒤 알리는 통로 (§4.9 · Stage 2). 인자는 넘긴 항목 수다.
    /// </summary>
    /// <remarks>
    /// <b>"복사했다"가 아니라 "넘겼다"까지만 말할 수 있다.</b> 복사를 셸이 하므로 성패도,
    /// 복사인지 이동인지도(<c>Shift</c> 를 누르면 셸이 이동을 고른다) 앱이 알 수 없다 —
    /// §4.2-2 의 "결과를 상태 바에 반드시 적는다"가 적용되지 않는 예외 자리다.
    /// <para>
    /// <see cref="TrashSelectionCommand"/> 와 같은 결로 타일을 만들 때 걸어 준다. 패널은
    /// 상태 바가 어디 있는지 모른다.
    /// </para>
    /// </remarks>
    public Action<int>? ReportExported { get; set; }

    /// <summary>
    /// 폴더 속성(§3.1)을 바꿨으니 워크스페이스를 저장해 달라고 알리는 통로 (§7.1 <c>folders[]</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="TrashSelectionCommand"/> 와 같은 결로 타일을 만들 때 걸어 준다 — 패널은
    /// 저장소가 어디 있는지 모른다. <b>새 저장 경로가 아니다</b>: <c>folders[].pinned</c> 와
    /// 배치가 이미 지나는 디바운스 저장에 얹는다.
    /// </remarks>
    public Action? SaveFolderEntry { get; set; }

    /// <summary>
    /// 우클릭 메뉴 "새 폴더로 등록"이 닿을 통로 (§4.11-1). 패널은 <c>Workspace</c> 를 모른다 —
    /// <see cref="TrashSelectionCommand"/> 와 같은 결로 타일을 만들 때 걸어 준다.
    /// </summary>
    /// <remarks>
    /// 인자는 우클릭한 항목(<see cref="FileItemViewModel"/>)이다. 하위 폴더까지 포함해 검색한
    /// 결과의 항목에도 그대로 걸린다(§4.10) — 그 목록이야말로 등록하고 싶은 폴더를 만나기 쉽다.
    /// </remarks>
    public System.Windows.Input.ICommand? RegisterFolderCommand { get; set; }

    /// <summary>
    /// 우클릭 메뉴 "이 폴더를 앵커로 지정"이 닿을 통로 (§4.6 · A군 ① D8). 패널은 <c>Workspace</c> 도
    /// 다른 등록도 모른다 — <see cref="TrashSelectionCommand"/> 와 같은 결로 타일을 만들 때 걸어 준다.
    /// </summary>
    /// <remarks>
    /// <b>커맨드가 아니라 델리게이트다</b> — <c>Workspace</c> 쪽은 <b>어느 타일의 앵커인지</b>를
    /// 알아야 하는데 우클릭이 넘겨 주는 것은 항목 하나뿐이라, 자기 자신(<c>this</c>)을 같이 넘긴다.
    /// <c>SaveFolderEntry</c> 와 같은 모양의 칸이다.
    /// <para>
    /// 반환값은 <b>앵커가 실제로 바뀌었는가</b>다. 거절(D9)이나 빈 칸이면 <c>false</c> 이고,
    /// 그때는 이 패널이 움직이지 않는다.
    /// </para>
    /// </remarks>
    public Func<FolderPanelViewModel, FileItemViewModel, bool>? SetAnchorFolder { get; set; }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "이 폴더를 앵커로 지정"이 닿을 통로 (§4.6 · D75).
    /// <see cref="SetAnchorFolder"/> 와 같은 결이지만 인자가 항목이 아니라 <b>경로 문자열</b>이다
    /// — 지금 보고 있는 폴더는 목록의 행이 아니라 그 목록의 컨테이너라
    /// <see cref="FileItemViewModel"/> 로 표현되지 않는다(B-0 §C.2).
    /// </summary>
    /// <remarks>반환값은 <see cref="SetAnchorFolder"/> 와 같다 — 앵커가 실제로 바뀌었는가.</remarks>
    public Func<FolderPanelViewModel, string, bool>? SetAnchorFolderToPath { get; set; }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "새 폴더로 등록"이 닿을 통로 (§4.6 · D75).
    /// <see cref="RegisterFolderCommand"/> 와 같은 결이지만 인자가 항목이 아니라 경로 문자열이다.
    /// </summary>
    public Action<string>? RegisterFolderPath { get; set; }

    /// <summary>
    /// 「즐겨찾기에 추가」가 닿을 통로 (2026-10-14 S3) — 항목 우클릭(폴더)과 「지금 보고 있는 폴더」
    /// 메뉴 둘 다. 패널은 즐겨찾기 저장소를 모른다 — <see cref="RegisterFolderPath"/> 와 같은 결이다.
    /// </summary>
    public Action<string>? AddFavoritePath { get; set; }

    /// <summary>
    /// 사용자가 이 패널을 클릭했다고 알리는 통로 (2026-10-14 S3 · 대상 타일). 패널은 자기가 어느
    /// 타일인지 모른다 — <see cref="CompareRequested"/> 와 같은 결이다.
    /// </summary>
    public Action<FolderPanelViewModel>? Activated { get; set; }

    /// <summary>뷰가 패널 안 왼쪽 클릭마다 부른다.</summary>
    public void NotifyActivated() => Activated?.Invoke(this);

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "새 폴더 만들기"가 띄우는 모달이 닿을 통로 (§4.6 · D75-c).
    /// 패널은 <c>IPrompt</c> 를 모른다 — <see cref="RenameOnDisk"/> 와 같은 결로 타일을 만들 때
    /// 걸어 준다.
    /// </summary>
    /// <remarks>
    /// 인자는 <b>모달에 채울 기본 이름</b>(<see cref="SuggestNewFolderName"/> 결과)이고,
    /// 반환값은 <b>사용자가 적은 이름</b>이다 — 취소면 <c>null</c>.
    /// </remarks>
    public Func<string, string?>? PromptForNewFolderName { get; set; }

    /// <summary>
    /// 우클릭 메뉴 「이름 바꾸기」와 <c>F2</c> 가 닿을 통로 (§4.6 309행 · A군 ②). 패널은 확인창도
    /// <c>Workspace</c> 도 모른다 — <see cref="SetAnchorFolder"/> 와 같은 결로 타일을 만들 때 걸어 준다.
    /// </summary>
    /// <remarks>
    /// <b>커맨드가 아니라 델리게이트다</b> — 반환값이 필요하다. 값은 <b>새 경로</b>이고
    /// 거절·취소·실패면 <c>null</c> 이라 그때는 이 패널이 움직이지 않는다. <c>bool</c> 로는
    /// 모자란 이유: 목록을 다시 읽은 뒤 <b>새 이름을 다시 골라</b> 줘야 하는데 그 경로는 이
    /// 통로 너머에서 만들어진다.
    /// <para>
    /// <b><see cref="SetAnchorFolder"/> 와 달리 패널 자신을 넘기지 않는다.</b> 앵커 지정은
    /// "어느 타일의 앵커를 옮기는가"라 타일이 인자였지만, 이름 바꾸기는 <b>바뀐 경로가 어느
    /// 등록의 앵커인가</b>(D12)를 <c>folders[]</c> 전체에서 찾는 일이라 타일이 답에 관여하지
    /// 않는다. 안 쓰는 인자를 모양 맞추기로 남기지 않는다.
    /// </para>
    /// </remarks>
    public Func<FileItemViewModel, string?>? RenameOnDisk { get; set; }

    /// <summary>
    /// 타일 간 비교(기능 후보 ①, 2026-09-18 설계) — <c>Ctrl+D</c>가 닿을 통로. 패널은 다른
    /// 타일이 뭐가 있는지, 지금 비교 중인지 모른다 — <see cref="RenameOnDisk"/> 와 같은 결로
    /// 타일을 만들 때 걸어 준다. 인자는 <b>요청한 패널 자신</b>이다 — 어느 타일인지는
    /// <see cref="MainViewModel"/> 이 <c>Tiles</c> 에서 찾는다(앵커 지정과 달리 패널 쪽에서
    /// 자기 타일을 모른다).
    /// </summary>
    public Action<FolderPanelViewModel>? CompareRequested { get; set; }

    /// <summary>
    /// 배치 리네임(기능 후보 ③, 2026-09-18 설계) — <c>Ctrl+F2</c>가 닿을 통로. 대상은
    /// <see cref="SelectedItems"/>(이 패널이 이미 안다) — <see cref="CompareRequested"/> 와
    /// 달리 패널 자신을 넘길 필요만 있다(어느 항목을 바꿀지는 통로 너머가 <c>panel.SelectedItems</c>
    /// 로 다시 읽는다). 실제 이름 변환 계산·<c>Move</c>·목록 새로고침은 전부 통로 너머
    /// (<see cref="MainViewModel"/>)에서 일어난다 — <see cref="RenameOnDisk"/> 와 같은 결이다.
    /// </summary>
    public Func<FolderPanelViewModel, Task>? BatchRenameRequested { get; set; }

    /// <summary>
    /// 우클릭 메뉴의 「내용 비교」(기능 후보 ①의 연장, 2026-09-18) — 이 항목이 닿을 통로.
    /// <see cref="CompareRequested"/> 와 같은 결로 패널 자신과 항목만 넘긴다 — 상대 타일
    /// 찾기·파일 읽기·diff 계산·창 띄우기는 전부 통로 너머(<see cref="MainViewModel"/>)에서
    /// 일어난다. 셸 메뉴가 <c>ShellMenuPolicy.AppItems</c> 에서 이미 [다름]인 행에만 이
    /// 항목을 끼우므로, 여기서 다시 <c>CompareStatus</c> 를 확인하지 않는다.
    /// </summary>
    public Action<FolderPanelViewModel, FileItemViewModel>? CompareContentRequested { get; set; }

    /// <summary>
    /// 붙여넣기가 닿을 통로 (§4.6 310행 · 330~336행 · A군 ③ C-2). 인자는 <b>붙여넣을 목적지</b>다.
    /// </summary>
    /// <remarks>
    /// 클립보드를 읽는 것부터 판정·실행·결과 보고까지 전부 이 통로 너머
    /// (<see cref="MainViewModel"/>)에서 일어난다 — 패널은 엔진도, 다른 조작이 도는 중인지도
    /// 모른다(<see cref="ShellDropHost"/> 와 같은 결이다).
    /// <para>
    /// <b><c>null</c> 목적지를 여기서 걸러내지 않는다.</b> 목적지가 없다는 것도 사용자에게 알릴
    /// 거절이고, 거절 문구를 상태 바에 적는 자리는 저쪽이다 — 패널은 상태 바가 어디 있는지 모른다.
    /// </para>
    /// </remarks>
    public Func<string?, Task>? PasteInto { get; set; }

    /// <summary>
    /// 셸 드래그가 이 타일 위에 있는가 — 파란 과녁 강조 (§4.2-2 의 폴더 행·대상함과 같은 결).
    /// </summary>
    /// <remarks>
    /// 런타임 전용이다. 저장하지 않는다.
    /// </remarks>
    [ObservableProperty]
    private bool isDropTarget;

    /// <summary>지금 놓으면 복사인가 — 과녁 글귀가 이것을 읽는다 (§4.2-2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropTargetText))]
    private bool dropTargetCopies;

    /// <summary>과녁 글귀 — <b>XAML 은 이것을 읽기만 한다</b>(§4.2-2 259행).</summary>
    public string DropTargetText => DropTargetCaption.For(DropTargetCopies);

    /// <summary>
    /// 셸 드롭의 목적지 — <b>지금 보고 있는 경로</b>이고 앵커가 아니다(§4.9 · §4.7
    /// <c>allVisible</c> 과 같은 기준: 화면에 보이는 그대로가 목적지다).
    /// </summary>
    /// <remarks>
    /// 아직 폴더를 안 띄운 순환 칸(<see cref="CurrentPath"/> 가 null)과 <b>접근 불가 폴더를
    /// 보고 있는 타일</b>은 받지 않는다 — 목록에서 지우지는 않되(§5) 놓을 자리도 아니다.
    /// </remarks>
    public string? ShellDropDestination => HasFailure ? null : CurrentPath;

        /// <summary>
        /// 아직 아무 폴더도 안 띄운 순환 칸. 전역 검색 타일은 <c>Entry</c> 가 늘 null 이어도
        /// 빈 상태가 아니다(D69-e 결정 3) — 이것이 검색 타일 모드가 새로 막는 유일한 자리다.
        /// </summary>
    public bool IsEmpty => Entry is null && !IsSearchTile;

    public string AnchorName => Entry is null
        ? string.Empty
        : (string.IsNullOrWhiteSpace(Entry.DisplayName) ? LastSegment(Entry.Path) : Entry.DisplayName!);

    /// <summary>
    /// 앵커 폴더의 표시 이름을 고쳤다 (§4.11-3). 헤더의 <see cref="AnchorName"/> 도 빵부스러기
    /// 첫 조각도 <c>Entry</c> 에서 계산되므로, 다시 열기 전에는 옛 이름이 남는다.
    /// </summary>
    public void NotifyAnchorName()
    {
        OnPropertyChanged(nameof(AnchorName));

        if (Entry is not null && CurrentPath is not null)
        {
            Breadcrumbs = BuildBreadcrumbs(Entry, CurrentPath, HidesAnchor);
        }
    }

    public bool HasFailure => Failure is not null;

    public string? FailureText => Failure is null ? null : Failure.Kind switch
    {
        FolderAccessFailureKind.NotFound => "경로가 없다",
        FolderAccessFailureKind.AccessDenied => "권한이 없어 열 수 없다",
        FolderAccessFailureKind.Unavailable => "접근 불가 — 네트워크 경로에 닿지 못한다",
        _ => "읽을 수 없다",
    };

    /// <summary>검색 타일은 <c>Entry</c> 가 없어도 늘 details 뷰다(D69-e 6-2) — 위치 열이 필요하다.</summary>
    public bool IsDetailsView => IsSearchTile || Entry?.ViewMode == FolderViewMode.Details;

    public bool IsListView => !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.List;

    public bool IsLargeIconView =>
        !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.LargeIcons;

    public bool IsExtraLargeIconView =>
        !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.ExtraLargeIcons;

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    /// <summary>재귀 검색이 걸린 상태 — details 뷰에 "위치" 열을 띄우는 조건이기도 하다.</summary>
    public bool IsRecursiveSearch => HasSearchText && IncludeSubfolders;

    /// <summary>
    /// "위치" 열(<c>SearchGridView</c>)이 필요한 상태 — 재귀 검색 결과나 전역 검색 결과처럼
    /// 서로 다른 폴더가 한 목록에 섞일 때(D69-e). 전역 검색은 늘 이 열이 필요하다(6-2).
    /// </summary>
    public bool NeedsLocationColumn => IsRecursiveSearch || IsSearchTile;

    /// <summary>"검색 안 함"(평소 목록)과 "결과 없음"을 구분한다. 전역 검색 상태 문구가 떠 있을 때는
    /// 그쪽이 대신 말하므로 겹치지 않는다(D69-e 결정 7).</summary>
    public bool ShowNoResults => HasSearchText && !IsSearching && DisplayItems.Count == 0 && EverythingStatusText is null;

    public bool HasSkippedFolders => SearchSkippedFolders > 0;

    public string SkippedFoldersText => $"{SearchSkippedFolders}개 폴더 건너뜀";

    public string? SearchStatusText => IsSearching
        ? $"검색 중… {DisplayItems.Count}건 · {SearchScannedFolders}개 폴더 탐색"
        : null;

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// 헤더 메타 — 개수 · 뷰 방식 · 정렬 (§3.2). <b>뷰는 이것을 통째로 그리지 않는다</b>:
    /// 가운데 뷰 방식 글자가 <see cref="ToggleViewModeCommand"/> 를 실행하는 버튼이라
    /// 세 조각(<see cref="MetaCountText"/> · <see cref="ViewModeText"/> ·
    /// <see cref="MetaSortText"/>)으로 나눠 그린다. 여기서 그 셋을 그대로 이어 붙이므로
    /// 이 문구와 화면에 그려지는 줄은 어긋날 수 없다.
    /// </summary>
    public string? MetaText =>
        Entry is null ? null : $"{MetaCountText}{ViewModeText}{MetaSortText}";

    /// <summary>
    /// 메타의 앞 조각 — 개수와 구분점("7개 · "). 전역 검색 결과가 상한에 잘렸으면 총계도
    /// 함께 적는다("120개 중 100개 · ") — 안 적으면 목록이 조용히 거짓말을 한다(D69-e 결정 6).
    /// </summary>
    public string MetaCountText => everythingTruncated
        ? $"{everythingTotalCount}개 중 {DisplayItems.Count}개 · "
        : $"{DisplayItems.Count}개 · ";

    // ── 헤더 1줄이 그리는 것들 (Phase 8-C ③) ─────────────────────────────────
    //
    // 8-C 이전에는 개수·뷰·정렬이 <b>2줄째 글자</b>였다. 지금은 셋이 1줄로 올라간다 —
    // 개수는 breadcrumb 오른쪽 글자로, 뷰와 정렬은 내비 아이콘 옆의 <b>아이콘</b>으로.
    // 그래서 2줄은 상태(앵커 밖 · 선택 · 읽는 중)가 있을 때만 뜨고, 없으면 통째로 접힌다
    // (<see cref="HasStatusLine"/>) — 그것이 헤더 높이를 줄인 방법이다.
    //
    // <b>값을 만드는 자리는 늘리지 않았다.</b> 아래 넷은 전부 기존 <see cref="ViewModeText"/> ·
    // <see cref="SortLabel"/> · <see cref="SortArrow"/> · <see cref="FoldersFirst"/> 에서
    // 파생된다. 그래서 아이콘과 <see cref="MetaText"/> 가 어긋날 수 없다.

    /// <summary>구분점 없는 개수 — 1줄에서 폴더 이름 옆에 붙는 조각.</summary>
    /// <remarks>
    /// <see cref="MetaCountText"/> 와 값이 같고 꼬리 구분점만 없다. 둘을 합치지 않는 이유는
    /// <see cref="MetaText"/> 가 세 조각을 <b>이어 붙인 것</b>이라는 규약을 그대로 두기
    /// 위해서다 — 구분점을 떼면 그 이음매가 "7개list" 가 된다.
    /// </remarks>
    public string ItemCountText => everythingTruncated
        ? $"{everythingTotalCount}개 중 {DisplayItems.Count}개"
        : $"{DisplayItems.Count}개";

    /// <summary>뷰 방식 아이콘 — list, details, 큰 아이콘, 아주 큰 아이콘.</summary>
    public string ViewModeIcon => Entry?.ViewMode switch
    {
        FolderViewMode.Details => "▦",
        FolderViewMode.LargeIcons => "▩",
        FolderViewMode.ExtraLargeIcons => "▣",
        _ => "☰",
    };

    public string ViewModeTip => Entry?.ViewMode switch
    {
        FolderViewMode.Details => "지금 details — 눌러서 큰 아이콘으로",
        FolderViewMode.LargeIcons => "지금 큰 아이콘 — 눌러서 아주 큰 아이콘으로",
        FolderViewMode.ExtraLargeIcons => "지금 아주 큰 아이콘 — 눌러서 list로 (컬럼 없이 이름만)",
        _ => "지금 list — 눌러서 details로 (이름·크기·수정 컬럼)",
    };

    /// <summary>
    /// 정렬 아이콘 — 방향 삼각형 하나, 통합 정렬이면 앞에 <c>∪</c> 가 붙는다 (Phase 8-C ③).
    /// </summary>
    /// <remarks>
    /// <b>화살표(↑/↓)를 쓰지 않는다</b>: 같은 아이콘 줄에 「상위 폴더 ↑」가 이미 있어서 둘이
    /// 섞인다. 삼각형은 이 줄에서 안 쓰던 모양이라 "정렬 방향"으로만 읽힌다.
    /// <para>
    /// <c>∪</c>(합집합)가 <b><see cref="FoldersFirst"/> 를 끈 상태가 갈 자리</b>다. 2줄째
    /// 메타에 붙던 " · 통합"이 그 줄과 함께 사라지는데, 저장되지 않는 상태라 화면이 말해
    /// 주지 않으면 알 길이 없다. <c>☰</c>(list 아이콘)와 헷갈리지 않게 가로줄 계열
    /// (<c>≡</c>)을 피해 고른 글리프다.
    /// </para>
    /// <para>
    /// <b>기준(이름·수정·크기·종류)은 아이콘에 없다.</b> 네 값에 글리프를 하나씩 붙이면
    /// 어휘표가 이 앱 쪽으로 비대해진다(familylook §6 이 막는 것). 기준은
    /// <see cref="SortTip"/> 과 열리는 메뉴의 체크가 말하고, 상세 뷰에서는 컬럼 머리글이
    /// 이미 「이름 ↑」처럼 적고 있다.
    /// </para>
    /// </remarks>
    public string SortIcon => (FoldersFirst ? string.Empty : "∪") + (CurrentSortDesc ? "▾" : "▴");

    /// <summary>정렬 아이콘의 툴팁 — 아이콘이 못 담는 기준과 통합 여부를 글자로 적는다.</summary>
    public string SortTip =>
        $"정렬: {SortLabel}{SortArrow}{(FoldersFirst ? string.Empty : " · 통합(폴더·파일 한 덩어리)")}"
        + " — 눌러서 기준 변경. 재귀 검색 중에는 잠긴다(결과가 찾은 순서대로 나온다).";

    /// <summary>
    /// 헤더 2줄째를 띄울 것이 있는가 (Phase 8-C ③ · 8-D ①).
    /// </summary>
    /// <remarks>
    /// 둘 다 <b>지금 벌어지고 있는 일</b>이라 평소에는 하나도 없다 — 그때 줄을 통째로 접어
    /// 헤더 높이를 돌려준다. 1x1 타일에서는 그 한 줄이 목록 두 행쯤 된다.
    /// <para>
    /// <b>선택은 8-D ① 에서 빠졌다.</b> 그것만 1줄로 올라갔기 때문이고, 이유는 이 줄이
    /// <b>있다 없다 하는 줄</b>이라서다 — 고르는 순간 헤더가 자라 목록이 손 아래에서 뛰었다.
    /// 남은 둘은 고르는 동작과 무관하게 바뀌므로 그 자리에 둔다.
    /// </para>
    /// </remarks>
    public bool HasStatusLine => ShowsAwayFromAnchor || IsLoading;

    /// <summary>
    /// 메타의 가운데 조각 — 뷰 방식. <b>눌러서 뒤집는 글자</b>다(§3.2 · Stage 3D).
    /// </summary>
    public string ViewModeText => Entry?.ViewMode switch
    {
        FolderViewMode.Details => "details",
        FolderViewMode.LargeIcons => "largeIcons",
        FolderViewMode.ExtraLargeIcons => "extraLargeIcons",
        _ => "list",
    };

    /// <summary>
    /// 메타의 뒤 조각 — 구분점과 정렬(" · 이름 ↑"). 통합 정렬이면 " · 통합"이 뒤에 붙는다
    /// (Phase 8-B ②) — 저장되지 않는 상태라 화면이 말해 주지 않으면 알 길이 없다.
    /// </summary>
    public string MetaSortText =>
        $" · {SortLabel}{SortArrow}{(FoldersFirst ? string.Empty : " · 통합")}";

    /// <summary>지금 정렬 기준의 이름 한 단어. 메뉴 라벨·컬럼 머리글과 <b>같은 어휘</b>다.</summary>
    public string SortLabel => LabelOf(CurrentSortBy);

    /// <summary>지금 정렬 방향 한 글자. 앞에 공백이 붙어 라벨에 그대로 이어 붙는다.</summary>
    public string SortArrow => CurrentSortDesc ? " ↓" : " ↑";

    // ── 상세 뷰 컬럼 머리글 (Phase 8-B ①) ────────────────────────────────────
    //
    // 머리글은 누르는 자리이자 <b>보여 주는 자리</b>다. 지금 정렬 컬럼에만 화살표가 붙으므로
    // "무엇으로 · 어느 방향" 이 목록 바로 위에서 읽힌다. 헤더 메타의 정렬 글자와 같은 사실을
    // 두 곳이 말하지만 값을 만드는 자리는 SortLabel·SortArrow 하나뿐이라 어긋날 수 없다.
    //
    // <b>GridViewColumn 은 DataContext 를 물려받지 않는다</b> — 시각 트리 밖이라서다. 그래서
    // 뷰는 이 문자열들을 HeaderTemplate 안에서 ListView 를 타고 올라와 읽는다(그 템플릿은
    // 시각 트리 안이다). 자세한 것은 Views/FolderPanelView.xaml 의 `SortableHeader` 주석이다.

    public string NameHeaderText => HeaderTextFor(SortBy.Name, "이름");

    public string SizeHeaderText => HeaderTextFor(SortBy.Size, "크기");

    public string ModifiedHeaderText => HeaderTextFor(SortBy.Modified, "수정");

    /// <summary>
    /// 폴더를 파일보다 항상 위에 둘지 (Phase 8-B ② · §7.1 <c>folders[].foldersFirst</c>).
    /// </summary>
    /// <remarks>
    /// 기본값 <c>true</c> 가 이 앱이 원래 하던 동작이다(탐색기 감각). 끄면 폴더와 파일이 한
    /// 덩어리로 정렬된다 — "오늘 건드린 것"을 수정 기준으로 볼 때 폴더가 위에 뭉쳐 있으면
    /// 방해가 된다는 것이 끄는 이유다.
    /// <para>
    /// <b>8-E ① 에서 저장 대상이 됐다.</b> 그 전에는 런타임 상태라 창을 다시 열면 켜진 채로
    /// 돌아왔다. 지금은 <see cref="SortBy"/>·<c>SortDesc</c>·<c>ViewMode</c> 와 같은 자리
    /// (<see cref="Entry"/>)에 있고 같은 통로로 저장된다 — <b>폴더마다</b>다.
    /// </para>
    /// <para>
    /// 값을 바꾸는 길은 <see cref="ToggleFoldersFirstCommand"/> 하나다 — 그것이 목록을 다시
    /// 읽는다. <see cref="Entry"/> 에 직접 쓰면 문구만 바뀌고 목록은 옛 순서로 남는다.
    /// </para>
    /// </remarks>
    public bool FoldersFirst => Entry?.FoldersFirst ?? true;

    /// <summary>
    /// "폴더 먼저"를 끄고 켠다 (Phase 8-B ② · 정렬 메뉴 아래칸).
    /// </summary>
    /// <remarks>
    /// <see cref="SetSortAsync"/> 와 <b>같은 결</b>이다 — 값을 바꾸고, 문구를 알리고, 저장하고,
    /// 목록을 다시 읽는다(8-E ① 에서 저장이 붙어 이제 다른 점이 없다). 선택도 비우지 않는다:
    /// 갈리는 것은 순서뿐이고 <see cref="RefreshAsync"/> 가 고른 것을 경로로 다시 잡아 준다(§4.8).
    /// <para>
    /// <b>저장은 <c>await</c> 앞이다</b> — <see cref="SetSortAsync"/> 와 같은 이유로, 고른 값은
    /// 열거가 취소되거나 폴더를 못 읽어도 남아야 한다.
    /// </para>
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleFoldersFirstAsync()
    {
        if (Entry is null)
        {
            return;
        }

        Entry.FoldersFirst = !Entry.FoldersFirst;
        NotifySortChanged();

        SaveFolderEntry?.Invoke();

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 상세 뷰 `크기` 열을 끄고 켠다 (Phase 15 · D79 · 컬럼 머리글 우클릭 메뉴, 진입점은
    /// D79-c 가 붙인다).
    /// </summary>
    /// <remarks>
    /// 폴더 타일은 <see cref="FolderEntry.ShowSize"/> 를 뒤집고 저장한다 —
    /// <see cref="ToggleFoldersFirstAsync"/> 와 같은 결이나, <b>목록을 다시 읽지 않는다.</b>
    /// 컬럼 표시는 열거 결과를 바꾸지 않는다 — 정렬·「폴더 먼저」와 달리 재조회할 이유가 없다.
    /// 검색 타일은 <see cref="searchShowSize"/> 만 뒤집고 저장하지 않는다 — <see cref="Entry"/>
    /// 가 없어 저장할 자리가 없다(D78 B-0 §5).
    /// </remarks>
    [RelayCommand]
    public void ToggleShowSize()
    {
        if (IsSearchTile)
        {
            searchShowSize = !searchShowSize;
        }
        else
        {
            if (Entry is null)
            {
                return;
            }

            Entry.ShowSize = !Entry.ShowSize;
            SaveFolderEntry?.Invoke();
        }

        OnPropertyChanged(nameof(CurrentShowSize));
    }

    /// <summary>
    /// 상세 뷰 `수정` 열을 끄고 켠다 (Phase 15 · D79).
    /// </summary>
    /// <remarks><see cref="ToggleShowSize"/> 와 같은 결·같은 이유다.</remarks>
    [RelayCommand]
    public void ToggleShowModified()
    {
        if (IsSearchTile)
        {
            searchShowModified = !searchShowModified;
        }
        else
        {
            if (Entry is null)
            {
                return;
            }

            Entry.ShowModified = !Entry.ShowModified;
            SaveFolderEntry?.Invoke();
        }

        OnPropertyChanged(nameof(CurrentShowModified));
    }

    /// <summary>
    /// 타일 하단 미리보기 칸을 끄고 켠다 (Phase 17 · D86 · §3.2 · 헤더 첫 줄 뷰 토글 옆,
    /// 진입점은 이 회차가 붙인다).
    /// </summary>
    /// <remarks><see cref="ToggleShowSize"/> 와 같은 결·같은 이유다. 목록을 다시 읽지 않는다.</remarks>
    [RelayCommand]
    public void ToggleShowPreview()
    {
        if (IsSearchTile)
        {
            searchShowPreview = !searchShowPreview;
        }
        else
        {
            if (Entry is null)
            {
                return;
            }

            Entry.ShowPreview = !Entry.ShowPreview;
            SaveFolderEntry?.Invoke();
        }

        OnPropertyChanged(nameof(CurrentShowPreview));
    }

    /// <summary>
    /// 미리보기 스플리터가 끌기를 끝냈을 때 비율을 저장한다(Phase 17 · D86). 커맨드가 아니다 —
    /// 코드비하인드가 <c>GridSplitter.DragCompleted</c> 에서 계산한 값을 그대로 넘긴다.
    /// </summary>
    /// <remarks>
    /// <b>범위 검사를 여기서 하지 않는다</b> — 폴더 타일 쪽은 <see cref="FolderEntry.PreviewRatio"/>
    /// 의 setter 가 이미 범위 밖 값을 기본값으로 떨어뜨린다(§7.1 v9 · D91). 여기서 또 검사하면
    /// 같은 규칙이 두 자리에 살아 하나만 고쳐지는 사고가 난다. <c>searchPreviewRatio</c> 는
    /// <see cref="FolderEntry.PreviewRatio"/> 처럼 스스로 클램프하는 setter 가 없는 평범한
    /// 필드라 — 검색 타일 갈래에서만 같은 판정을 그대로 옮겨 쓴다.
    /// </remarks>
    public void SetPreviewRatio(double ratio)
    {
        if (IsSearchTile)
        {
            searchPreviewRatio = ratio is >= 0.0 and <= 1.0 ? ratio : FolderEntry.DefaultPreviewRatio;
        }
        else
        {
            if (Entry is null)
            {
                return;
            }

            Entry.PreviewRatio = ratio;
            SaveFolderEntry?.Invoke();
        }

        OnPropertyChanged(nameof(CurrentPreviewRatio));
    }

    /// <summary>
    /// `위치` 열을 끄고 켠다 (Phase 15 · D79 · D80-b). <see cref="Entry"/> 쪽에는 이 열 자체가
    /// 없어 — <see cref="NeedsLocationColumn"/> 이 거짓이면(그 열이 붙는 뷰가 아니면) 아무
    /// 일도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// <b>보이는 조건과 먹는 조건이 같아야 한다</b>(D80-b B-1) — 컬럼 메뉴의 `위치` 항목
    /// <c>Visibility</c> 가 이미 <see cref="NeedsLocationColumn"/> 이다(`FolderPanelView.xaml:106`).
    /// 예전엔 <c>IsSearchTile</c> 하나만 봐서, **재귀 검색**(폴더 타일이 검색 중인 것 — 검색
    /// 타일이 아니다) 중에는 메뉴 항목이 보이는데도 눌러도 아무 일이 없었다.
    /// <para>
    /// <b>가드를 없애지는 않는다</b>(B-2) — 열이 없는 뷰(재귀 검색도 검색 타일도 아닌 평소
    /// 폴더 타일)에서 값이 뒤집히면, 나중에 검색을 켰을 때 끈 적 없는 열이 꺼진 채로 나온다.
    /// </para>
    /// <para>
    /// <b>여전히 저장하지 않는다</b>(B-4) — 재귀 검색 중인 폴더 타일에는 <see cref="Entry"/> 가
    /// 있지만, `위치` 는 검색 중일 때만 존재하는 열이지 그 폴더의 항구적 성질이 아니다.
    /// <c>Entry</c> 에 넣으면 검색을 안 할 때도 값이 남는다(§7.1 v8).
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void ToggleShowPosition()
    {
        if (!NeedsLocationColumn)
        {
            return;
        }

        searchShowPosition = !searchShowPosition;
        OnPropertyChanged(nameof(CurrentShowPosition));
    }

    /// <summary>
    /// 정렬이 바뀌었다고 알린다 — <b>메타 두 조각과 컬럼 머리글 셋</b>.
    /// </summary>
    /// <remarks>
    /// 한 자리에 모아 둔다. <c>[NotifyPropertyChangedFor]</c> 는 <c>entry</c> <b>필드</b>에 붙어
    /// 있어서 같은 <see cref="Entry"/> 안의 값만 바꾸면 아무 통지도 나가지 않는다 — 빠뜨리면
    /// 목록만 바뀌고 머리글 화살표가 옛 컬럼에 남는다.
    /// </remarks>
    private void NotifySortChanged()
    {
        OnPropertyChanged(nameof(FoldersFirst));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(SortArrow));
        OnPropertyChanged(nameof(MetaSortText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(NameHeaderText));
        OnPropertyChanged(nameof(SizeHeaderText));
        OnPropertyChanged(nameof(ModifiedHeaderText));
        OnPropertyChanged(nameof(SortIcon));
        OnPropertyChanged(nameof(SortTip));
    }

    /// <summary>
    /// 지금 정렬 컬럼이면 화살표를 붙이고, 아니면 라벨만. 폴더를 안 띄운 순환 칸(<c>Entry</c>
    /// 가 null)은 <b>어느 컬럼과도 매치하지 않는다</b> — 화살표 없이 라벨만 그린다(기존
    /// 동작). <see cref="CurrentSortBy"/> 는 그 경우 편의상 <c>SortBy.Name</c> 으로
    /// 떨어지므로(<see cref="SortLabel"/> 용) 여기서는 직접 <c>Entry?.SortBy</c> 를 본다.
    /// <b>재귀 검색 중에는 어느 컬럼에도 화살표를 붙이지 않는다</b>(D69-g 결정 4·6-1) —
    /// 그 결과는 찾은 순서 그대로라 정렬돼 있지 않다. 헤더 메타의 정렬 글자를 잠그는
    /// <see cref="IsRecursiveSearch"/> 판정을 그대로 재사용한다 — 새 규칙을 만들지 않는다.
    /// </summary>
    private string HeaderTextFor(SortBy column, string label)
    {
        if (IsRecursiveSearch)
        {
            return label;
        }

        SortBy? activeSort = IsSearchTile ? searchSortBy : Entry?.SortBy;
        return activeSort == column ? label + SortArrow : label;
    }

    private static string LabelOf(SortBy sort) => sort switch
    {
        SortBy.Modified => "수정",
        SortBy.Size => "크기",
        SortBy.Type => "종류",
        _ => "이름",
    };

    /// <summary>
    /// 뷰 방식을 순환한다 — <c>list</c> → <c>details</c> → <c>largeIcons</c> →
    /// <c>extraLargeIcons</c> → <c>list</c>.
    /// </summary>
    /// <remarks>
    /// <b>표시 다섯이 함께 움직여야 한다</b>: 네 <c>ListView</c> 의 <c>Visibility</c> 를 가르는
    /// <see cref="IsDetailsView"/>·<see cref="IsListView"/>·<see cref="IsLargeIconView"/>·
    /// <see cref="IsExtraLargeIconView"/>와
    /// 헤더 메타 문구다. 하나만
    /// 갱신하면 목록과 문구가 어긋난다.
    /// <para>
    /// <b>선택은 비운다.</b> 네 <c>ListView</c> 가 각자 선택을 들고 있어서, 그대로 두면 보이게
    /// 된 목록에는 아무것도 안 골라져 있는데 헤더는 "N개 선택"을 말하고 <c>Del</c> 이 그것을
    /// 지운다. v1 은 전환을 넘어 선택을 잇지 않는다. 뷰 방식을 먼저 뒤집고 나서 비우므로,
    /// 떠나는 목록은 이미 <c>Collapsed</c> 다 — 그 목록이 내는 <c>SelectionChanged</c> 는
    /// 뷰의 <c>!list.IsVisible</c> 문턱에서 걸러지고, 새로 보이게 된 목록은 선택이 비어 있어
    /// 아무 이벤트도 내지 않는다.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void ToggleViewMode()
    {
        if (Entry is null)
        {
            return;
        }

        ApplyViewMode(Entry.ViewMode switch
        {
            FolderViewMode.List => FolderViewMode.Details,
            FolderViewMode.Details => FolderViewMode.LargeIcons,
            FolderViewMode.LargeIcons => FolderViewMode.ExtraLargeIcons,
            _ => FolderViewMode.List,
        });
    }

    /// <summary>
    /// 「더 보기」 메뉴의 보기 방식 하위 메뉴(목록/자세히)가 고르는 값(Phase 20 · D119) —
    /// <see cref="SetSortAsync"/> 가 정렬 기준을 <c>CommandParameter</c> 로 받는 것과 같은 결이다.
    /// <see cref="ToggleViewMode"/>(헤더 아이콘이 쓰던 것, 시험이 이미 물고 있어 그대로 둔다)와
    /// 로직을 <see cref="ApplyViewMode"/> 로 나눠 쓴다. 이미 그 방식이면 아무 일도 안 한다 —
    /// 메뉴에서 체크된 항목을 다시 눌러도 선택·저장이 헛되이 일어나지 않게.
    /// </summary>
    [RelayCommand]
    public void SetViewMode(FolderViewMode mode)
    {
        if (Entry is null || Entry.ViewMode == mode)
        {
            return;
        }

        ApplyViewMode(mode);
    }

    private void ApplyViewMode(FolderViewMode mode)
    {
        Entry!.ViewMode = mode;

        OnPropertyChanged(nameof(IsDetailsView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsLargeIconView));
        OnPropertyChanged(nameof(IsExtraLargeIconView));
        OnPropertyChanged(nameof(ViewModeText));
        OnPropertyChanged(nameof(ViewModeIcon));
        OnPropertyChanged(nameof(ViewModeTip));
        OnPropertyChanged(nameof(MetaText));

        ClearSelection();

        SaveFolderEntry?.Invoke();
    }

    /// <summary>
    /// 정렬 기준을 고른다 — 헤더 메타의 정렬 글자 메뉴 (§3.2 · §7.1 <c>folders[].sortBy</c>).
    /// </summary>
    /// <remarks>
    /// <b>같은 기준을 다시 고르면 방향을 뒤집는다.</b> 다른 기준으로 옮기면 오름차순으로
    /// 되돌린다 — 크기 내림차순을 보다가 이름으로 옮겼는데 이름이 거꾸로 서 있으면 놀란다.
    /// <para>
    /// <b>선택을 비우지 않는다.</b> 뷰 방식 토글(<see cref="ToggleViewMode"/>)과 달리 여기서는
    /// 네 <c>ListView</c> 의 <c>Visibility</c> 가 갈리지 않는다. 목록은 <see cref="RefreshAsync"/>
    /// 가 다시 읽고 그 안에서 고른 것을 <b>경로로</b> 다시 잡아 준다(§4.8) — 정렬을 바꿨다고
    /// 고른 파일이 날아가면 매크로를 만들려던 손이 헛돈다.
    /// </para>
    /// <para>
    /// 통지를 손으로 부른다. <c>[NotifyPropertyChangedFor]</c> 는 <c>entry</c> <b>필드</b>에
    /// 붙어 있어서 같은 <see cref="Entry"/> 안의 값만 바꾸면 아무 통지도 나가지 않는다 —
    /// 그대로 두면 목록만 바뀌고 헤더 문구가 옛 값으로 남는다.
    /// </para>
    /// <para>
    /// <b>저장은 <c>await</c> 앞이다.</b> 고른 정렬은 열거가 취소되거나 폴더를 못 읽어도
    /// 남아야 한다. 저장 자체는 <c>pinned</c>·배치가 이미 쓰는 디바운스 통로에 얹는다(§7.1).
    /// </para>
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SetSortAsync(SortBy sort)
    {
        if (IsSearchTile)
        {
            // 검색 타일은 Entry 가 없다 — 저장하지 않는 자체 상태를 바꾸고 재질의만 한다
            // (D69-f 결정 1·2·3). 받아 온 목록을 지역 정렬하지 않는다 — Everything 이 상한
            // 밖까지 반영된 정렬을 주는 유일한 길이라서다.
            if (searchSortBy == sort)
            {
                searchSortDesc = !searchSortDesc;
            }
            else
            {
                searchSortBy = sort;
                searchSortDesc = false;
            }

            NotifySortChanged();
            RestartSearch();
            return;
        }

        if (Entry is null)
        {
            return;
        }

        if (Entry.SortBy == sort)
        {
            Entry.SortDesc = !Entry.SortDesc;
        }
        else
        {
            Entry.SortBy = sort;
            Entry.SortDesc = false;
        }

        NotifySortChanged();

        SaveFolderEntry?.Invoke();

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>앵커를 벗어나 있는가. 왼쪽 리스트 행에도 이 상태를 표시한다(§3.2).</summary>
    public bool IsAwayFromAnchor =>
        Entry is not null && CurrentPath is not null && !PathEquals(CurrentPath, Normalize(Entry.Path));

    /// <summary>
    /// 앵커를 사용자에게 보이지 않는 패널인가 — 1회용 창(2026-10-14 2차 A1). 닫으면 끝나는 창이라
    /// 「돌아갈 자리」가 뜻이 없다. <b>보이는 것만</b> 감춘다: 「앵커 밖」 글자 · ⌂ 버튼 · Alt+Home ·
    /// 「앵커로 지정」 메뉴, 그리고 브레드크럼은 늘 전체 경로다. <see cref="GoToAnchorAsync"/> 의 동작
    /// 자체는 그대로다 — 같은 폴더 재클릭·처음 띄우기가 안에서 부른다.
    /// </summary>
    public bool HidesAnchor { get; init; }

    /// <summary>「앵커 밖」 글자를 보일지 — XAML 은 이것만 본다.</summary>
    public bool ShowsAwayFromAnchor => IsAwayFromAnchor && !HidesAnchor;

    /// <summary>앵커를 다루는 메뉴·버튼을 보일지.</summary>
    public bool ShowsAnchorActions => !HidesAnchor;

    /// <summary>
    /// ⌂ 버튼과 Alt+Home 의 진입점. 앵커를 감춘 패널에서는 아무 일도 안 한다 —
    /// <see cref="GoToAnchorAsync"/> 를 막으면 내부 호출까지 막히므로 진입점을 따로 둔다.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task GoToAnchorFromUserAsync() =>
        HidesAnchor ? Task.CompletedTask : GoToAnchorAsync();

    /// <summary>예: "지금 src › Recipe 안".</summary>
    public string? LocationHint
    {
        get
        {
            if (!IsAwayFromAnchor)
            {
                return null;
            }

            // 1회용 창(A1) — 「앵커」 낱말 없이 지금 자리만 말한다.
            if (HidesAnchor)
            {
                return $"지금 {CurrentPath}";
            }

            return Breadcrumbs.Count > 0 && Breadcrumbs[0].IsAnchor
                ? $"지금 {string.Join(" › ", Breadcrumbs.Select(b => b.Name))} 안"
                : $"앵커 밖: {CurrentPath}";
        }
    }

    public bool CanGoUp => CurrentPath is not null && IoDirectory.GetParent(CurrentPath) is not null;

    /// <summary>이 폴더를 이 패널에 띄운다. 앵커에서 시작하고, 히스토리에 쌓인다.</summary>
    public Task ShowAsync(FolderEntry entry) =>
        NavigateAsync(entry, Normalize(entry.Path), pushHistory: true);

    public void Clear()
    {
        _cts?.Cancel();
        _searchCts?.Cancel();
        _back.Clear();
        _forward.Clear();
        NotifyHistoryChanged();
        Entry = null;
        CurrentPath = null;
        Items = [];
        DisplayItems = [];
        Breadcrumbs = [];
        Failure = null;
        IsLoading = false;
        SearchText = string.Empty;
        ClearSelection();
    }

    // ── 이동 ─────────────────────────────────────────────────────────────────

    /// <summary>앵커로 복귀 — breadcrumb 첫 조각 / <c>Alt+Home</c> / 리스트 재클릭 공용(§4.6).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task GoToAnchorAsync() =>
        Entry is null ? Task.CompletedTask : NavigateAsync(Entry, Normalize(Entry.Path), pushHistory: true);

    /// <summary>상위 폴더로(<c>Alt+↑</c>). 앵커 위로도 갈 수 있다(§4.6 — 이동은 자유).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task GoUpAsync()
    {
        if (Entry is null || CurrentPath is null)
        {
            return Task.CompletedTask;
        }

        var parent = IoDirectory.GetParent(CurrentPath);
        return parent is null
            ? Task.CompletedTask
            : NavigateAsync(Entry, Normalize(parent.FullName), pushHistory: true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task NavigateToAsync(string? path) =>
        Entry is null || string.IsNullOrWhiteSpace(path)
            ? Task.CompletedTask
            : NavigateAsync(Entry, Normalize(path), pushHistory: true);

    /// <summary>
    /// 뒤로 — 마우스 <c>XButton1</c> / <c>Alt+←</c>. <b>상위 이동과 다르다</b>:
    /// 직전에 있던 곳으로 간다(형제 폴더에서 뒤로 = 그 전 위치).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack), AllowConcurrentExecutions = true)]
    public async Task GoBackAsync()
    {
        if (_back.Count == 0)
        {
            return;
        }

        var target = _back.Pop();
        PushCurrentTo(_forward);
        NotifyHistoryChanged();
        await NavigateAsync(target.Entry, target.Path, pushHistory: false).ConfigureAwait(true);
    }

    /// <summary>앞으로 — 마우스 <c>XButton2</c> / <c>Alt+→</c>.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward), AllowConcurrentExecutions = true)]
    public async Task GoForwardAsync()
    {
        if (_forward.Count == 0)
        {
            return;
        }

        var target = _forward.Pop();
        PushCurrentTo(_back);
        NotifyHistoryChanged();
        await NavigateAsync(target.Entry, target.Path, pushHistory: false).ConfigureAwait(true);
    }

    /// <summary>수동 갱신(<c>F5</c>) · 창 활성화 · "다시 시도" 공용 (§8 · §5).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task RefreshAsync() =>
        CurrentPath is null ? Task.CompletedTask : LoadAsync(CurrentPath);

    /// <summary>
    /// 목록 읽기가 <b>끝날 때마다</b> 낸다 — 성공·실패 가리지 않는다.
    /// 감시자가 이 신호로 자기가 죽었는지 다시 본다(§8 · D37). 이 뷰모델은 감시자를 모른다.
    /// </summary>
    /// <remarks>
    /// <b>취소로 버려진 읽기는 내지 않는다</b> — 그 호출은 결과를 쓰지 않았으므로 「끝났다」가
    /// 아니다. 접근 불가로 <b>실패</b>한 읽기는 낸다: 「다시 시도」가 지나는 길이 바로 그것이다.
    /// </remarks>
    public event EventHandler? LoadCompleted;

    /// <summary>더블클릭: 폴더면 안에서 진입(히스토리 쌓임), 파일이면 OS 기본 프로그램(§4.5).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenAsync(FileItemViewModel? item)
    {
        if (item is null || Entry is null)
        {
            return Task.CompletedTask;
        }

        if (item.IsDirectory)
        {
            return NavigateAsync(Entry, Normalize(item.FullPath), pushHistory: true);
        }

        // 앱 내 미리보기는 비목표 — OS 기본 프로그램에 맡긴다(§1.2).
        Report(_shell.Open(item.FullPath), $"열 수 없다: {item.Name}");
        return Task.CompletedTask;
    }

    // ── 우클릭 메뉴 (경로 하나로 끝나는 읽기들) ──────────────────────────────

    /// <summary>항목의 전체 절대 경로를 클립보드로.</summary>
    [RelayCommand]
    public void CopyItemPath(FileItemViewModel? item)
    {
        if (item is not null)
        {
            Report(_clipboard.SetText(item.FullPath), "경로를 복사하지 못했다");
        }
    }

    /// <summary>지금 보고 있는 폴더 경로를 클립보드로(패널 헤더용).</summary>
    [RelayCommand]
    public void CopyCurrentPath()
    {
        if (CurrentPath is not null)
        {
            Report(_clipboard.SetText(CurrentPath), "경로를 복사하지 못했다");
        }
    }

    /// <summary>지금 보고 있는 폴더를 탐색기에서 연다. 앵커가 아니라 현재 이동 경로다.</summary>
    [RelayCommand]
    public void RevealCurrentFolder()
    {
        if (CurrentPath is not null)
        {
            Report(
                _shell.RevealInExplorer(CurrentPath, isDirectory: true),
                "현재 폴더를 탐색기에서 열지 못했다");
        }
    }

    /// <summary>탐색기에서 열기 — 파일이면 그 파일을 선택한 채로.</summary>
    [RelayCommand]
    public void RevealItem(FileItemViewModel? item)
    {
        if (item is not null)
        {
            Report(
                _shell.RevealInExplorer(item.FullPath, item.IsDirectory),
                $"탐색기에서 열지 못했다: {item.Name}");
        }
    }

    /// <summary>
    /// 셸 속성 대화상자를 띄운다 — <b>읽기다</b>. 앱 상태도 <c>folders[]</c> 도 바뀌지 않는다.
    /// </summary>
    /// <remarks>
    /// <see cref="CopyItemPath"/>·<see cref="RevealItem"/> 과 같은 층이다 — 경로 하나면 되는 일이라
    /// <see cref="MainViewModel"/> 을 거치지 않는다. 창을 그리는 것은 셸이고 앱은 넘기기만 한다.
    /// </remarks>
    [RelayCommand]
    public void ShowItemProperties(FileItemViewModel? item)
    {
        if (item is not null)
        {
            Report(_shell.ShowProperties(item.FullPath), $"속성을 열지 못했다: {item.Name}");
        }
    }

    /// <summary>
    /// 우클릭한 하위 폴더를 <b>이 타일의 앵커</b>로 지정한다 (§4.6 305행 · A군 ① D8).
    /// </summary>
    /// <remarks>
    /// <b>쓰기다</b> — 위의 셋과 달리 <c>folders[]</c> 가 바뀌고 저장 경로를 탄다. 그 일은 전부
    /// <see cref="SetAnchorFolder"/> 통로 너머(<see cref="MainViewModel"/>)에서 일어나고, 여기서는
    /// <b>바뀐 뒤 새 앵커로 옮겨 오는 것</b>만 한다 — <c>currentPath</c> 가 옛 위치에 남아 있으면
    /// 방금 앵커로 삼은 폴더가 "앵커 밖"으로 보인다.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SetAnchorToItemAsync(FileItemViewModel? item)
    {
        // 파일에는 뜻이 없다. 메뉴가 이미 감추지만 여기서도 막는다 — 통로가 하나 더 생기면
        // 메뉴의 판정만으로는 못 막는다(RegisterFolder 와 같은 결).
        if (item is null || !item.IsDirectory || SetAnchorFolder is null)
        {
            return;
        }

        if (!SetAnchorFolder(this, item))
        {
            return;
        }

        await GoToAnchorAsync().ConfigureAwait(true);
    }

    // ── 「지금 보고 있는 폴더」 메뉴 (§4.6 · D75 · Phase 13) ────────────────────
    //
    // 대상이 목록의 행이 아니라 그 목록의 컨테이너(CurrentPath 자신)라 위 항목 우클릭
    // 커맨드들과는 별도 진입점이다. 이 회차(D75-b)는 이 계층만 연다 — 메뉴 XAML 배선은
    // D75-c 다(B-0 §C.2 · §D).

    /// <summary>
    /// 「새 폴더 만들기」모달에 채울 기본 이름 — §4.3 의 이름 규칙을 그대로 태운다(B-0 §D.3).
    /// 같은 이름이 있으면 "새 폴더 (2)".
    /// </summary>
    /// <remarks>
    /// <see cref="FileOperationEngine.PreviewUniqueName"/> 은 <b>이미 겹친다는 것을 아는 자리</b>
    /// 에서만 쓴다 — n=2 부터 세므로 원래 이름 자체가 비어 있는지는 스스로 보지 않는다(§4.3
    /// 「둘 다 두기」는 항상 겹친 뒤에만 불린다). 그래서 겹치는지부터 여기서 직접 본다.
    /// <para>모달을 띄우는 것은 이 회차가 아니다 — 기본값을 계산하는 자리만 연다.</para>
    /// </remarks>
    public static string SuggestNewFolderName(string parentPath)
    {
        const string baseName = "새 폴더";
        var candidate = IoPath.Combine(parentPath, baseName);

        return IoDirectory.Exists(candidate) || System.IO.File.Exists(candidate)
            ? IoPath.GetFileName(FileOperationEngine.PreviewUniqueName(candidate))
            : baseName;
    }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "새 폴더 만들기" (§4.6 · D75). 실제 생성은
    /// <see cref="FolderCreator"/>(Core) 가 한다 — 저장되는 값이 늘지 않으므로
    /// <see cref="MainViewModel"/> 통로를 거치지 않는다(B-0 §D).
    /// </summary>
    /// <remarks>
    /// 검색 타일에는 만들 자리가 없다 — <see cref="IsSearchTile"/> 을 <b>직접</b> 본다.
    /// <c>CurrentPath == null</c> 부작용에 기대지 않는다(D75-b-3) — 그 부작용은 이름이 의도를
    /// 말하지 않는다.
    /// </remarks>
    [RelayCommand]
    public void CreateNewFolder(string? name)
    {
        if (IsSearchTile)
        {
            Reject("검색 타일에는 만들 자리가 없다");
            return;
        }

        if (CurrentPath is not { } parent)
        {
            return;
        }

        if (FolderCreator.Create(parent, name) is { } reason)
        {
            Reject($"폴더를 만들지 못했다 — {reason}");
        }

        // 만든 폴더가 목록에 나타나는 것은 파일 시스템 감시자가 한다(§8) — 여기서 다시 읽지 않는다.
    }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴 「새 폴더 만들기」 항목이 실제로 부르는 커맨드 (§4.6 ·
    /// D75-5 · D75-c). 기본 이름을 제안하고, 모달로 묻고, 취소가 아니면 <see cref="CreateNewFolder"/>
    /// 로 넘긴다.
    /// </summary>
    /// <remarks>
    /// <b>모달 자체는 <see cref="PromptForNewFolderName"/> 통로 너머(<see cref="MainViewModel"/>)에서
    /// 뜬다</b> — <c>IPrompt</c> 는 패널이 모른다(<see cref="RenameOnDisk"/> 와 같은 결). 검색
    /// 타일 가드를 여기서도 먼저 본다 — 모달을 띄우기 전에 걸러야 뜨지도 않을 모달이 안 뜬다.
    /// 실제 생성·거절 통지는 <see cref="CreateNewFolder"/> 가 그대로 한다 — 이 메서드가 두 번
    /// 하지 않는다.
    /// </remarks>
    [RelayCommand]
    public void CreateNewFolderInteractive()
    {
        if (IsSearchTile)
        {
            Reject("검색 타일에는 만들 자리가 없다");
            return;
        }

        if (CurrentPath is not { } parent || PromptForNewFolderName is not { } prompt)
        {
            return;
        }

        if (prompt(SuggestNewFolderName(parent)) is not { } typed)
        {
            // 취소 — 아무 일도 하지 않는다(폴더도 통지도 저장도 없다).
            return;
        }

        CreateNewFolder(typed);
    }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "이 폴더를 앵커로 지정" (§4.6 · D75). 대상은
    /// <see cref="CurrentPath"/> 자신이다 — 항목 우클릭의 <see cref="SetAnchorToItemAsync"/> 와
    /// 다른 진입점이다.
    /// </summary>
    /// <remarks>검색 타일 가드는 <see cref="CreateNewFolder"/> 와 같은 이유·같은 판정이다.</remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SetAnchorToCurrentAsync()
    {
        if (IsSearchTile)
        {
            Reject("검색 타일에는 앵커로 지정할 폴더가 없다");
            return;
        }

        if (CurrentPath is not { } path || SetAnchorFolderToPath is null)
        {
            return;
        }

        if (!SetAnchorFolderToPath(this, path))
        {
            return;
        }

        await GoToAnchorAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 「지금 보고 있는 폴더」 메뉴의 "새 폴더로 등록" (§4.6 · D75). 대상은
    /// <see cref="CurrentPath"/> 자신이다 — 항목 우클릭의 <see cref="RegisterFolderCommand"/> 와
    /// 다른 진입점이다.
    /// </summary>
    [RelayCommand]
    public void RegisterCurrentFolder()
    {
        if (IsSearchTile)
        {
            Reject("검색 타일에는 등록할 폴더가 없다");
            return;
        }

        if (CurrentPath is { } path)
        {
            RegisterFolderPath?.Invoke(path);
        }
    }

    /// <summary>「지금 보고 있는 폴더」 메뉴의 "즐겨찾기에 추가" (2026-10-14 S3).</summary>
    [RelayCommand]
    public void AddCurrentFolderToFavorites()
    {
        if (IsSearchTile)
        {
            Reject("검색 타일에는 즐겨찾기에 넣을 폴더가 없다");
            return;
        }

        if (CurrentPath is { } path)
        {
            AddFavoritePath?.Invoke(path);
        }
    }

    /// <summary>항목 우클릭(폴더)의 "즐겨찾기에 추가" (2026-10-14 S3). 파일에는 메뉴가 안 뜬다.</summary>
    public void AddItemToFavorites(FileItemViewModel? item)
    {
        if (item is { IsDirectory: true })
        {
            AddFavoritePath?.Invoke(item.FullPath);
        }
    }

    /// <summary>
    /// 우클릭한 항목의 <b>디스크 이름</b>을 바꾼다 (§4.6 309행 · A군 ② B-1).
    /// </summary>
    /// <remarks>
    /// <b>폴더도 파일도 대상이다</b> — 등록·앵커 지정과 달리 파일에도 뜻이 있으므로 메뉴가
    /// 감추지 않고 여기서도 막지 않는다.
    /// <para>
    /// 이름을 묻는 것부터 실제 <c>Move</c> 까지 전부 <see cref="RenameOnDisk"/> 통로 너머
    /// (<see cref="MainViewModel"/>)에서 일어나고, 여기서는 <b>바뀐 뒤 목록을 다시 읽어 새 이름을
    /// 다시 고르는 것</b>만 한다. 옛 경로는 사라졌으므로 <see cref="LoadAsync"/> 의 "경로로 다시
    /// 잡기"(§4.8)가 이 항목을 되찾지 못한다 — 그것만이 이 자리에 남는 일이다.
    /// </para>
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RenameItemAsync(FileItemViewModel? item)
    {
        if (item is null || Entry is null || RenameOnDisk is null)
        {
            return;
        }

        if (RenameOnDisk(item) is not { } renamed)
        {
            return;
        }

        await RefreshAsync().ConfigureAwait(true);

        // 재귀 검색 결과를 보고 있으면 새 이름이 그 결과에 없을 수 있다 — 그때는 고르지 않는다.
        if (DisplayItems.FirstOrDefault(i => PathEquals(i.FullPath, renamed)) is { } moved)
        {
            SelectedItems = [moved];
            SelectionRestored?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// <c>F2</c> — 지금 고른 것 하나의 이름을 바꾼다. <b>메뉴와 같은 경로를 부른다</b>
    /// (진입점만 둘이고 동작은 하나다).
    /// </summary>
    /// <remarks>
    /// 여럿 고른 상태는 거절한다 — 어느 것을 말하는지 정할 근거가 없다. 아무것도 안 고른
    /// <c>F2</c> 는 조용히 지난다(누를 대상이 없는 것은 거절이 아니다).
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task RenameSelectionAsync()
    {
        if (SelectedItems.Count == 1)
        {
            return RenameItemAsync(SelectedItems[0]);
        }

        if (SelectedItems.Count > 1)
        {
            Reject($"이름 바꾸기는 하나만 고른 뒤에 — 지금 {SelectedItems.Count}개다");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>Ctrl+D</c> — 이 타일을 다른 타일과 비교한다(기능 후보 ①). 실제 판정·짝짓기는
    /// <see cref="CompareRequested"/> 너머(<see cref="MainViewModel"/>)에서 일어난다 —
    /// 이 패널은 자기가 어느 타일인지, 다른 타일이 뭐가 있는지 모른다.
    /// </summary>
    [RelayCommand]
    private void StartCompare() => CompareRequested?.Invoke(this);

    /// <summary>
    /// 우클릭 「내용 비교」(기능 후보 ①의 연장) — 셸 메뉴가 이 커맨드를 [다름]으로 표시된
    /// 행에만 붙인다(<c>FolderPanelView</c> 우클릭 핸들러).
    /// </summary>
    [RelayCommand]
    private void CompareContent(FileItemViewModel item) => CompareContentRequested?.Invoke(this, item);

    /// <summary>
    /// <c>Ctrl+F2</c> — 지금 고른 항목들의 이름을 규칙으로 한꺼번에 바꾼다(기능 후보 ③).
    /// F2(<see cref="RenameSelectionAsync"/>)와 달리 여럿을 요구한다 — 하지만 하나만 골라도
    /// 거절하지 않는다(미리보기 표가 한 줄뿐일 때 실행해도 해될 게 없다). 아무것도 안
    /// 고르면 F2 처럼 조용히 지난다.
    /// </summary>
    [RelayCommand]
    private Task BatchRenameSelection()
    {
        if (SelectedItems.Count == 0 || BatchRenameRequested is null)
        {
            return Task.CompletedTask;
        }

        return BatchRenameRequested(this);
    }

    // ── 클립보드로 복사 (§4.6 310행 · A군 ③ C-1) ─────────────────────────────
    //
    // 「경로 복사」와 다른 일이다 — 그쪽은 경로 <b>글자</b>를 싣고 이쪽은 셸 파일 포맷을 싣는다.
    // <b>파일 시스템은 건드리지 않는다</b>: 실제 복사는 붙여넣는 쪽(탐색기든 이 앱이든)이 한다.

    /// <summary>
    /// 지금 고른 것 <b>전부</b>를 셸 파일 클립보드로 (<c>Ctrl+C</c> · 우클릭 「복사」).
    /// </summary>
    /// <remarks>
    /// <b>이 패널 안에서 끝난다</b> — 클립보드 서비스가 이미 여기 있고 엔진도 <c>Workspace</c> 도
    /// 필요 없다. 「휴지통으로 삭제」와 달리 통로 칸이 없는 것이 그 때문이다.
    /// 대상을 <b>고른 것 전부</b>로 잡는 것은 그 「휴지통으로 삭제」와 같다.
    /// </remarks>
    [RelayCommand]
    public void CopySelectionToClipboard()
    {
        if (SelectedItems.Count == 0)
        {
            // 클립보드를 건드리지 않는다 — 빈 것으로 덮으면 아까 복사해 둔 것이 사라진다.
            Reject("복사할 것이 없다 — 먼저 고른 뒤에 Ctrl+C");
            return;
        }

        Report(
            _clipboard.SetFiles(
                [.. SelectedItems.Select(i =>
                    new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))]),
            "클립보드에 올리지 못했다");
    }

    /// <summary>
    /// 지금 고른 것 <b>전부</b>를 <b>이동</b> 클립보드로 (<c>Ctrl+X</c>).
    /// </summary>
    /// <remarks>
    /// <b>여기서도 파일 시스템은 건드리지 않는다</b> — 실제 이동은 붙여넣는 쪽(탐색기든 이
    /// 앱이든)이 한다. 원본이 사라지는 것은 이 명령이 아니라 그 붙여넣기다.
    /// <para>
    /// 「복사」(<see cref="CopySelectionToClipboard"/>)와 <b>다른 것은 실리는 효과 하나뿐</b>이다
    /// — 대상도 통로도 같고 <c>Preferred DropEffect</c> 만 이동이 된다(D36 · D20 폐기).
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void CutSelectionToClipboard()
    {
        if (SelectedItems.Count == 0)
        {
            // 클립보드를 건드리지 않는다 — 빈 것으로 덮으면 아까 복사해 둔 것이 사라진다.
            Reject("잘라낼 것이 없다 — 먼저 고른 뒤에 Ctrl+X");
            return;
        }

        Report(
            _clipboard.SetFiles(
                [.. SelectedItems.Select(i =>
                    new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))],
                move: true),
            "잘라낸 것을 클립보드에 올리지 못했다");
    }

    /// <summary>
    /// 클립보드의 것을 <b>이 타일이 지금 보고 있는 폴더</b>로 (<c>Ctrl+V</c> · breadcrumb 우클릭
    /// 「붙여넣기」).
    /// </summary>
    /// <remarks>
    /// 목적지는 앵커가 아니라 <see cref="ShellDropDestination"/> 이다(§4.9 442행 — 셸 드롭이 쓰는
    /// 바로 그 값이다). <c>null</c> 이어도 그대로 넘긴다 — 거절 문구는 통로 너머가 적는다.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task PasteIntoCurrentAsync() =>
        PasteInto is { } paste ? paste(ShellDropDestination) : Task.CompletedTask;

    // ── 검색 ─────────────────────────────────────────────────────────────────

    /// <summary>검색창을 편다 (「검색」 메뉴 항목 체크·포커스(<c>Ctrl+F</c>)). 검색 타일은 늘 펴져 있어 아무 일도 안 한다(D69-e 6-2).</summary>
    [RelayCommand]
    public void ExpandSearch()
    {
        if (IsSearchTile)
        {
            return;
        }

        IsSearchExpanded = true;
    }

    /// <summary>
    /// 명시적으로 닫는다 (✕ · 「더 보기」 메뉴의 「검색」 체크 해제). <b>검색어도 같이 비운다</b> —
    /// 입력줄이 사라졌는데 목록만 걸러진 채로 남으면 왜 이것만 보이는지 알 수 없다.
    /// 포커스가 빠지는 경우(<see cref="CollapseSearchIfEmpty"/>)와 달리 검색어가 있어도 닫는다.
    /// 검색 타일은 늘 펴져 있어 아무 일도 안 한다(D69-e 6-2).
    /// </summary>
    [RelayCommand]
    public void CloseSearch()
    {
        if (IsSearchTile)
        {
            return;
        }

        SearchText = string.Empty;
        IsSearchExpanded = false;
    }

    /// <summary>
    /// 「더 보기」 메뉴의 「검색」 체크 항목이 부르는 토글(Phase 20 · D119) — 예전 헤더 🔍
    /// 아이콘의 <c>OnSearchToggleClick</c>(코드비하인드)이 하던 판정과 같다: 열려 있으면
    /// 닫고, 닫혀 있으면 편다. 포커스를 주는 것(<c>FocusSearch</c>)은 뷰가 하는 일이라
    /// 여기서는 하지 않는다 — <c>Ctrl+F</c> 경로는 그대로 <c>FocusSearch</c> 를 쓴다.
    /// </summary>
    [RelayCommand]
    public void ToggleSearchExpanded()
    {
        if (IsSearchExpanded)
        {
            CloseSearch();
        }
        else
        {
            ExpandSearch();
        }
    }

    /// <summary>
    /// 포커스가 빠졌을 때 접는다. <b>검색어가 남아 있으면 접지 않는다</b> —
    /// 결과를 보는 중에 접히면 무엇을 보고 있는지 알 수 없게 된다.
    /// 검색 타일은 늘 펴져 있어 아무 일도 안 한다(D69-e 6-2).
    /// </summary>
    [RelayCommand]
    public void CollapseSearchIfEmpty()
    {
        if (IsSearchTile)
        {
            return;
        }

        if (!HasSearchText)
        {
            IsSearchExpanded = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        RestartSearch();
        NotifySortChanged();
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        RestartSearch();
        NotifySortChanged();
    }

    partial void OnItemsChanged(IReadOnlyList<FileItemViewModel> value) => RestartSearch();

    /// <summary>
    /// 목록을 갈아끼우기 <b>직전</b>에 FPU 마스크를 되돌린다 (F-1 그물).
    /// </summary>
    /// <remarks>
    /// <c>DisplayItems</c> 대입이 곧 네 <c>ListView</c> 의 <c>ItemsSource</c> 통째 교체이고,
    /// 그 다음이 레이아웃 패스다. 셸 확장이 FPU 마스크를 풀고 갔으면 그 레이아웃에서 터지므로
    /// 이 길목에서 한 번 더 켠다 — <see cref="FpuGuard"/> 는 안 풀려 있으면 아무것도 하지
    /// 않아 뜨거운 경로에서도 값싸다.
    /// <para>
    /// 원래 이 자리에 <b>어느 교체가 레이아웃을 못 넘겼는지</b> 짝으로 찍는 계측이 있었다
    /// (E-2·E-4). 원인이 확정돼 걷어냈다 — 마스크를 되돌리는 손만 남았다.
    /// </para>
    /// </remarks>
    partial void OnDisplayItemsChanged(IReadOnlyList<FileItemViewModel> value)
    {
        using var fpu = FpuGuard.Enter("그물:DisplayItems");
    }

    /// <summary>검색어·체크·목록이 바뀔 때마다 이전 검색을 즉시 취소하고 다시 잡는다.</summary>
    private void RestartSearch()
    {
        CancelSearch();

        var query = SearchText;

        // 전역 검색 타일은 완전히 다른 길로 간다(D69-e) — 로컬 열거·재귀 검색은 CurrentPath 가
        // 늘 null 이라 어차피 못 쓴다(결정 2). "하위 포함" 개념도 없다 — Everything 은 이미
        // 전체를 본다.
        if (IsSearchTile)
        {
            RestartEverythingSearch(query);
            return;
        }

        // 검색어를 비우면 원래 목록으로 복귀.
        if (string.IsNullOrEmpty(query))
        {
            SearchScannedFolders = 0;
            SearchSkippedFolders = 0;
            DisplayItems = Items;
            return;
        }

        // 체크 해제 = 이미 열거된 것만 이름 부분일치로 즉석 필터. I/O 없음.
        if (!IncludeSubfolders)
        {
            SearchScannedFolders = 0;
            SearchSkippedFolders = 0;
            DisplayItems = [.. Items.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
            return;
        }

        if (CurrentPath is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        // 디바운스를 기다리기 전에 "검색 중"으로 표시한다 — 타이핑하고 나서
        // 잠깐 아무 일도 없는 것처럼 보이지 않게, 그리고 "검색 대기 중"과
        // "결과 없음"이 섞이지 않게.
        IsSearching = true;
        _ = SearchRecursivelyAsync(CurrentPath, query, cts);
    }

    private async Task SearchRecursivelyAsync(string root, string query, CancellationTokenSource cts)
    {
        var token = cts.Token;

        try
        {
            // 타이핑 중에 트리를 반복해서 걷지 않도록 잠깐 기다린다.
            await Task.Delay(RecursiveSearchDebounceMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_searchCts, cts) || CurrentPath is null)
        {
            return;
        }

        var stats = new FolderSearchStats();
        var results = new ObservableCollection<FileItemViewModel>();
        var buffer = new List<FileItemViewModel>();
        var clock = Stopwatch.StartNew();

        SearchScannedFolders = 0;
        SearchSkippedFolders = 0;
        DisplayItems = results;
        IsSearching = true;

        // 결과가 한 건도 안 나오는 동안에도 진행 표시가 움직여야 하므로 따로 센다.
        var progress = TrackSearchProgressAsync(stats, token);

        void Flush()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            foreach (var hit in buffer)
            {
                results.Add(hit);
            }

            buffer.Clear();
            clock.Restart();
            OnPropertyChanged(nameof(MetaText));
            OnPropertyChanged(nameof(MetaCountText));
            OnPropertyChanged(nameof(ItemCountText));
            OnPropertyChanged(nameof(SearchStatusText));
        }

        try
        {
            await foreach (var hit in _enumerator
                .SearchAsync(root, query, stats, token)
                .ConfigureAwait(true))
            {
                buffer.Add(new FileItemViewModel(hit.Item, hit.RelativeFolder, ExtensionGlyphs, ShellIconService));

                if (buffer.Count >= SearchFlushBatch || clock.ElapsedMilliseconds >= SearchFlushMs)
                {
                    Flush();
                }
            }

            Flush();
        }
        catch (OperationCanceledException)
        {
            // 더 새로운 검색이나 폴더 이동이 이 검색을 대체했다.
        }
        catch (Exception ex)
        {
            _reportError?.Invoke($"검색 실패: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                // 참조를 먼저 끊는다. 안 그러면 아래에서 Dispose 한 CTS 를
                // 나중에 CancelSearch 가 붙잡고 ObjectDisposedException 을 낸다.
                _searchCts = null;
                IsSearching = false;
                SearchScannedFolders = stats.FoldersScanned;
                SearchSkippedFolders = stats.FoldersSkipped;
                OnPropertyChanged(nameof(ShowNoResults));
            }

            // 진행 표시 타이머를 세운다.
            await cts.CancelAsync().ConfigureAwait(true);
            await progress.ConfigureAwait(true);
            cts.Dispose();
        }
    }

    /// <summary>
    /// 전역 검색 타일의 검색 시작(D69-e 6-2). 로컬 재귀 검색(<see cref="SearchRecursivelyAsync"/>)
    /// 과 달리 <c>CurrentPath</c> 가 필요 없다 — Everything 은 색인 전체를 본다.
    /// </summary>
    private void RestartEverythingSearch(string query)
    {
        EverythingStatusText = null;
        everythingTruncated = false;
        everythingTotalCount = 0;

        if (string.IsNullOrEmpty(query) || EverythingSearchService is null)
        {
            DisplayItems = [];
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        _ = RunEverythingSearchAsync(query, cts);
    }

    private async Task RunEverythingSearchAsync(string query, CancellationTokenSource cts)
    {
        var token = cts.Token;

        try
        {
            // 타이핑 중에 Everything 을 반복해서 부르지 않도록 잠깐 기다린다.
            await Task.Delay(EverythingSearchDebounceMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_searchCts, cts) || EverythingSearchService is null)
        {
            return;
        }

        EverythingSearchResult result;

        try
        {
            // D69-f — 컬럼 머리로 고른 정렬 기준을 Everything 에 그대로 넘긴다. 기본은
            // 이름 오름차순(searchSortBy 필드 초기값).
            result = await EverythingSearchService
                .SearchAsync(query, searchSortBy, searchSortDesc, EverythingMaxResults, token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
            }

            _reportError?.Invoke($"검색 실패: {ex.Message}");
            return;
        }

        if (!ReferenceEquals(_searchCts, cts))
        {
            // 더 새로운 검색이 이 검색을 대체했다.
            return;
        }

        _searchCts = null;

        if (result.Outcome != EverythingSearchOutcome.Ok)
        {
            EverythingStatusText = DescribeOutcome(result.Outcome);
            DisplayItems = [];
            return;
        }

        // 잘림 여부를 DisplayItems 대입보다 먼저 정한다 — MetaCountText·ItemCountText 의
        // NotifyPropertyChangedFor 가 DisplayItems 대입에 걸려 있으므로, 그 대입이 일어날 때
        // 이미 최신 값이어야 문구와 목록이 한 번에 갈린다(D69-e 결정 6).
        everythingTruncated = result.Truncated;
        everythingTotalCount = result.TotalCount;

        DisplayItems = [.. result.Items.Select(item => new FileItemViewModel(
            item,
            IoPath.GetDirectoryName(item.FullPath) ?? string.Empty,
            ExtensionGlyphs,
            ShellIconService))];
    }

    /// <summary>
    /// 검색 실패 갈래를 안내 문구로 바꾼다(D69-e 결정 7) — 설정 창의 <see cref="EverythingStatus.
    /// Describe"/> 와 같은 말을 쓴다. <see cref="EverythingSearchOutcome.Failed"/> 는
    /// <see cref="EverythingAvailability"/> 에 짝이 없는 질의 자체의 실패라 따로 적는다.
    /// </summary>
    private static string DescribeOutcome(EverythingSearchOutcome outcome) => outcome switch
    {
        EverythingSearchOutcome.DllMissing =>
            new EverythingStatus(EverythingAvailability.DllMissing, null).Describe(),
        EverythingSearchOutcome.NotRunning =>
            new EverythingStatus(EverythingAvailability.NotRunning, null).Describe(),
        EverythingSearchOutcome.IndexNotLoaded =>
            new EverythingStatus(EverythingAvailability.IndexNotLoaded, null).Describe(),
        EverythingSearchOutcome.Failed => "검색에 실패했다 — 다시 시도하세요.",
        _ => "알 수 없는 상태다.",
    };

    private async Task TrackSearchProgressAsync(FolderSearchStats stats, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true))
            {
                SearchScannedFolders = stats.FoldersScanned;
                SearchSkippedFolders = stats.FoldersSkipped;
            }
        }
        catch (OperationCanceledException)
        {
            // 검색이 끝났다.
        }
    }

    private void CancelSearch()
    {
        var previous = _searchCts;
        _searchCts = null;

        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 그 검색은 이미 스스로 끝나고 정리됐다.
        }

        if (IsSearching)
        {
            IsSearching = false;
        }
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private async Task NavigateAsync(FolderEntry entry, string path, bool pushHistory)
    {
        // 같은 자리로 다시 가는 것은 히스토리에 쌓지 않는다 — 뒤로가 제자리걸음이 되면
        // 고장난 것처럼 느껴진다(앵커에서 앵커 복귀를 다시 누르는 경우).
        var samePlace = ReferenceEquals(Entry, entry)
                        && CurrentPath is not null
                        && PathEquals(CurrentPath, path);

        if (pushHistory && !samePlace)
        {
            PushCurrentTo(_back);
            _forward.Clear();
            NotifyHistoryChanged();
        }

        // 폴더가 바뀌면 이전 검색은 즉시 취소하고 검색창을 비운다 — 새 위치에서
        // 예상 못 한 트리 걷기가 다시 시작되지 않게(기준점은 항상 지금 보는 폴더다).
        // 여기서 DisplayItems 는 잠깐 이전 목록을 가리키지만, 바로 아래 LoadAsync 가
        // Items 를 갈아치우면서 다시 맞춰진다.
        CancelSearch();
        SearchText = string.Empty;

        // 다른 폴더를 보는데 이전 폴더의 선택이 남아 있으면 그 선택으로 파일을 옮기게 된다.
        ClearSelection();

        Entry = entry;

        // breadcrumb 을 먼저 세운다. CurrentPath 변경 통지를 받은 쪽(왼쪽 리스트 행)이
        // LocationHint 를 읽을 때 이미 새 경로 기준이어야 한다.
        Breadcrumbs = BuildBreadcrumbs(entry, path, HidesAnchor);
        CurrentPath = path;

        await LoadAsync(path).ConfigureAwait(true);
    }

    private void PushCurrentTo(Stack<HistoryPosition> stack)
    {
        if (Entry is not null && CurrentPath is not null)
        {
            stack.Push(new HistoryPosition(Entry, CurrentPath));
        }
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 거절(사용자가 잘못 눌렀고 화면이 이미 답을 보여준다)을 알린다. 스스로 사라지는 경고다 —
    /// 실패(<see cref="_reportError"/>)와 달리 남지 않는다.
    /// </summary>
    /// <remarks>
    /// 통로를 안 받았으면 실패 통로로 떨어진다 — 거절이 조용히 사라지는 것보다 낫다.
    /// </remarks>
    private void Reject(string text) => (_reportRejection ?? _reportError)?.Invoke(text);

    private void Report(string? error, string prefix)
    {
        if (error is not null)
        {
            _reportError?.Invoke($"{prefix} — {error}");
        }
    }

    /// <summary>
    /// 셸 컨텍스트 메뉴가 실패했다고 상태 바에 올리는 통로 (§4.6 · D32).
    /// </summary>
    /// <remarks>
    /// 우클릭 메뉴는 뷰가 직접 띄운다(COM STA 라 UI 스레드에서만 되는 일이다) — 그래서 실패도
    /// 뷰가 먼저 잡는다. 여기는 <b>그 글자를 상태 바로 넘기는 한 칸</b>일 뿐이고 판정은 하지
    /// 않는다. 확장이 던진 말을 우리 문구로 덮지 않는다.
    /// </remarks>
    public void ReportShellMenuFailure(string message) => _reportError?.Invoke(message);

    /// <summary>
    /// 셸 컨텍스트 메뉴를 <b>띄우지 않고 거절</b>했다고 알리는 통로 (F-05).
    /// </summary>
    /// <remarks>
    /// 판정과 대상 목록은 뷰가 쥐고 있으므로(COM STA) 거절도 뷰가 먼저 잡는다 — 여기는
    /// <see cref="ReportShellMenuFailure"/> 와 마찬가지로 <b>글자를 넘기는 한 칸</b>이고,
    /// 통로 자체는 「이름 바꾸기는 하나만 고른 뒤에」와 <b>같은 <c>Reject</c></b> 다.
    /// 실패 통로로 보내지 않는다 — 거절은 스스로 사라지고 실패는 남는다.
    /// </remarks>
    public void ReportShellMenuRejection(string message) => Reject(message);

    private async Task LoadAsync(string path)
    {
        if (Entry is null)
        {
            return;
        }

        // 이전 열거를 취소한다 — 큰 폴더나 죽은 네트워크 경로를 기다리다가
        // 다음 클릭이 막히지 않게 한다.
        //
        // 취소만 하고 <b>Dispose 는 하지 않는다</b>. 그 CTS 는 아직 await 에 걸려 있는
        // 이전 호출의 것이고, 남이 쓰는 것을 여기서 버리면 그쪽이 깨어나 자기 토큰을 읽는
        // 순간 ObjectDisposedException 으로 죽는다(실제로 그렇게 터졌다 — 파일 이동 뒤
        // 결과 대화상자가 창을 재활성화해 새로고침이 겹치면서). 각 호출이 자기 것만 정리한다.
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);

        // 토큰은 지금 꺼내 둔다 — Dispose 된 CTS 는 Token 게터부터 던진다.
        var token = cts.Token;

        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 그 열거는 이미 스스로 끝나고 정리됐다.
        }

        IsLoading = true;

        // 이 호출이 목록을 실제로 갈아끼웠는가. 취소로 버려진 경로에서는 켜지지 않는다 —
        // 아래 LoadCompleted 를 「완료마다 정확히 한 번」으로 묶는 것이 이 지역 변수다.
        var completed = false;

        try
        {
            var listing = await _enumerator
                .ListAsync(path, Entry.SortBy, Entry.SortDesc, FoldersFirst, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || !ReferenceEquals(_cts, cts))
            {
                return;
            }

            // 고른 것은 <b>경로로</b> 기억한다. 아래에서 Items 를 갈아끼우면 뷰가 선택을
            // 비우고 그 통지가 SelectedItems 까지 지우므로, 새 목록의 객체로 다시 잡아 준다.
            var chosen = SelectedItems.Count == 0
                ? null
                : SelectedItems.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Failure = listing.Failure;
            Items = [.. listing.Items.Select(i =>
                new FileItemViewModel(i, extensionGlyphs: ExtensionGlyphs, shellIconService: ShellIconService))];

            if (chosen is not null)
            {
                // 사라진 파일은 자연히 빠진다 — 옮기고 난 뒤의 원본이 그렇다.
                SelectedItems = [.. Items.Where(i => chosen.Contains(i.FullPath))];
                SelectionRestored?.Invoke(this, EventArgs.Empty);
            }

            completed = true;
        }
        catch (OperationCanceledException)
        {
            // 더 새로운 열거가 이 자리를 차지했다. 조용히 버린다.
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                Interlocked.CompareExchange(ref _cts, null, cts);
                IsLoading = false;
            }

            cts.Dispose();
        }

        if (completed)
        {
            LoadCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── 경로 표시 헬퍼 ───────────────────────────────────────────────────────

    /// <param name="fullPath">
    /// 앵커로 접지 않고 늘 드라이브 뿌리부터 전체 경로를 낼지 — 1회용 창(A1). 아래 「앵커 밖」 갈래와 같은 모양이다.
    /// </param>
    internal static IReadOnlyList<BreadcrumbSegment> BuildBreadcrumbs(FolderEntry entry, string currentPath, bool fullPath = false)
    {
        var anchor = Normalize(entry.Path);
        var anchorName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? LastSegment(entry.Path)
            : entry.DisplayName!;

        if (fullPath)
        {
            return FullPathBreadcrumbs(currentPath);
        }

        if (PathEquals(currentPath, anchor))
        {
            return [new BreadcrumbSegment(anchorName, anchor, true)];
        }

        // 앵커 아래면 "앵커 이름 › 하위…" 로 접어 보여준다.
        if (IsUnder(currentPath, anchor))
        {
            var segments = new List<BreadcrumbSegment> { new(anchorName, anchor, true) };
            Accumulate(segments, anchor, currentPath[anchor.Length..]);
            return segments;
        }

        // 앵커 위나 옆으로 나간 경우엔 접을 기준이 없으므로 전체 경로를 그대로 보여준다.
        return FullPathBreadcrumbs(currentPath);
    }

    /// <summary>드라이브(또는 UNC) 뿌리부터 전체 경로 조각. 어느 조각도 앵커가 아니다.</summary>
    private static IReadOnlyList<BreadcrumbSegment> FullPathBreadcrumbs(string currentPath)
    {
        var root = IoPath.GetPathRoot(currentPath);
        if (string.IsNullOrEmpty(root))
        {
            return [new BreadcrumbSegment(currentPath, currentPath, false)];
        }

        var outside = new List<BreadcrumbSegment> { new(root, root, false) };
        Accumulate(outside, root, currentPath[root.Length..]);
        return outside;
    }

    private static void Accumulate(List<BreadcrumbSegment> segments, string basePath, string relative)
    {
        var accumulated = basePath;
        foreach (var part in relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = IoPath.Combine(accumulated, part);
            segments.Add(new BreadcrumbSegment(part, accumulated, false));
        }
    }

    /// <summary>끝의 구분자를 떼되 <c>D:\</c> 같은 루트는 건드리지 않는다.</summary>
    private static string Normalize(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed.Length != path.Length && IsRoot(path) ? path : trimmed;
    }

    private static bool IsRoot(string path) =>
        string.Equals(IoPath.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string parent) =>
        path.Length > parent.Length
        && path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
        && Separators.Contains(path[parent.Length]);

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        var name = IoPath.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
