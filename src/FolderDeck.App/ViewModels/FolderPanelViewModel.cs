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
        Action<string>? reportError = null,
        Action<string>? reportRejection = null)
    {
        _enumerator = enumerator;
        _shell = shell;
        _clipboard = clipboard;
        _reportError = reportError;
        _reportRejection = reportRejection;
        this.isRotating = isRotating;
    }

    private readonly record struct HistoryPosition(FolderEntry Entry, string Path);

    /// <summary>
    /// 순환 칸이면 true(점선), 상시 칸이면 false(실선). §2.3
    /// <para>배치 편집에서 칸 성격을 토글하면 바뀌므로 읽기 전용이 아니다(Phase 2).</para>
    /// </summary>
    [ObservableProperty]
    private bool isRotating;

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
    private FolderEntry? entry;

    /// <summary>지금 보고 있는 경로. <b>런타임 전용 — JSON 에 저장하지 않는다</b>(§4.6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAwayFromAnchor))]
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
    private string searchText = string.Empty;

    /// <summary>"하위 포함" 체크. 끄면 즉석 필터, 켜면 재귀 검색.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecursiveSearch))]
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
    public FileDropPayload? BuildDragPayload() =>
        CurrentPath is { } path && SelectedItems.Count > 0
            ? new FileDropPayload(
                path,
                [.. SelectedItems.Select(i => new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))])
            : null;

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

    /// <summary>
    /// 셸 드롭의 목적지 — <b>지금 보고 있는 경로</b>이고 앵커가 아니다(§4.9 · §4.7
    /// <c>allVisible</c> 과 같은 기준: 화면에 보이는 그대로가 목적지다).
    /// </summary>
    /// <remarks>
    /// 아직 폴더를 안 띄운 순환 칸(<see cref="CurrentPath"/> 가 null)과 <b>접근 불가 폴더를
    /// 보고 있는 타일</b>은 받지 않는다 — 목록에서 지우지는 않되(§5) 놓을 자리도 아니다.
    /// </remarks>
    public string? ShellDropDestination => HasFailure ? null : CurrentPath;

        /// <summary>아직 아무 폴더도 안 띄운 순환 칸.</summary>
    public bool IsEmpty => Entry is null;

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
            Breadcrumbs = BuildBreadcrumbs(Entry, CurrentPath);
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

    public bool IsDetailsView => Entry?.ViewMode == FolderViewMode.Details;

    public bool IsListView => Entry is not null && Entry.ViewMode == FolderViewMode.List;

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    /// <summary>재귀 검색이 걸린 상태 — details 뷰에 "위치" 열을 띄우는 조건이기도 하다.</summary>
    public bool IsRecursiveSearch => HasSearchText && IncludeSubfolders;

    /// <summary>"검색 안 함"(평소 목록)과 "결과 없음"을 구분한다.</summary>
    public bool ShowNoResults => HasSearchText && !IsSearching && DisplayItems.Count == 0;

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

    /// <summary>메타의 앞 조각 — 개수와 구분점("7개 · ").</summary>
    public string MetaCountText => $"{DisplayItems.Count}개 · ";

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
    public string ItemCountText => $"{DisplayItems.Count}개";

    /// <summary>뷰 방식 아이콘 — list 는 줄 셋, details 는 격자 (§1.2 유니코드 글리프만).</summary>
    public string ViewModeIcon => Entry?.ViewMode == FolderViewMode.Details ? "▦" : "☰";

    public string ViewModeTip => Entry?.ViewMode == FolderViewMode.Details
        ? "지금 details — 눌러서 list 로 (컬럼 없이 이름만)"
        : "지금 list — 눌러서 details 로 (이름·크기·수정 컬럼)";

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
    public string SortIcon => (FoldersFirst ? string.Empty : "∪") + (Entry?.SortDesc == true ? "▾" : "▴");

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
    public bool HasStatusLine => IsAwayFromAnchor || IsLoading;

    /// <summary>
    /// 메타의 가운데 조각 — 뷰 방식. <b>눌러서 뒤집는 글자</b>다(§3.2 · Stage 3D).
    /// </summary>
    public string ViewModeText => Entry?.ViewMode == FolderViewMode.Details ? "details" : "list";

    /// <summary>
    /// 메타의 뒤 조각 — 구분점과 정렬(" · 이름 ↑"). 통합 정렬이면 " · 통합"이 뒤에 붙는다
    /// (Phase 8-B ②) — 저장되지 않는 상태라 화면이 말해 주지 않으면 알 길이 없다.
    /// </summary>
    public string MetaSortText =>
        $" · {SortLabel}{SortArrow}{(FoldersFirst ? string.Empty : " · 통합")}";

    /// <summary>지금 정렬 기준의 이름 한 단어. 메뉴 라벨·컬럼 머리글과 <b>같은 어휘</b>다.</summary>
    public string SortLabel => LabelOf(Entry?.SortBy ?? SortBy.Name);

    /// <summary>지금 정렬 방향 한 글자. 앞에 공백이 붙어 라벨에 그대로 이어 붙는다.</summary>
    public string SortArrow => Entry?.SortDesc == true ? " ↓" : " ↑";

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

    /// <summary>지금 정렬 컬럼이면 화살표를 붙이고, 아니면 라벨만.</summary>
    private string HeaderTextFor(SortBy column, string label) =>
        Entry?.SortBy == column ? label + SortArrow : label;

    private static string LabelOf(SortBy sort) => sort switch
    {
        SortBy.Modified => "수정",
        SortBy.Size => "크기",
        SortBy.Type => "종류",
        _ => "이름",
    };

    /// <summary>
    /// 뷰 방식을 뒤집는다 — <c>list</c> ↔ <c>details</c> (§3.2 · §7.1 <c>folders[].viewMode</c>).
    /// </summary>
    /// <remarks>
    /// <b>표시 셋이 함께 움직여야 한다</b>: 두 <c>ListView</c> 의 <c>Visibility</c> 를 가르는
    /// <see cref="IsDetailsView"/>·<see cref="IsListView"/> 와 헤더 메타 문구다. 하나만
    /// 갱신하면 목록과 문구가 어긋난다.
    /// <para>
    /// <b>선택은 비운다.</b> 두 <c>ListView</c> 가 각자 선택을 들고 있어서, 그대로 두면 보이게
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

        Entry.ViewMode = Entry.ViewMode == FolderViewMode.Details
            ? FolderViewMode.List
            : FolderViewMode.Details;

        OnPropertyChanged(nameof(IsDetailsView));
        OnPropertyChanged(nameof(IsListView));
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
    /// 두 <c>ListView</c> 의 <c>Visibility</c> 가 갈리지 않는다. 목록은 <see cref="RefreshAsync"/>
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

    /// <summary>예: "지금 src › Recipe 안".</summary>
    public string? LocationHint
    {
        get
        {
            if (!IsAwayFromAnchor)
            {
                return null;
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

    /// <summary>검색창을 편다 (🔍 클릭·포커스·<c>Ctrl+F</c>).</summary>
    [RelayCommand]
    public void ExpandSearch() => IsSearchExpanded = true;

    /// <summary>
    /// 명시적으로 닫는다 (✕ · 🔍 다시 누르기). <b>검색어도 같이 비운다</b> —
    /// 입력줄이 사라졌는데 목록만 걸러진 채로 남으면 왜 이것만 보이는지 알 수 없다.
    /// 포커스가 빠지는 경우(<see cref="CollapseSearchIfEmpty"/>)와 달리 검색어가 있어도 닫는다.
    /// </summary>
    [RelayCommand]
    public void CloseSearch()
    {
        SearchText = string.Empty;
        IsSearchExpanded = false;
    }

    /// <summary>
    /// 포커스가 빠졌을 때 접는다. <b>검색어가 남아 있으면 접지 않는다</b> —
    /// 결과를 보는 중에 접히면 무엇을 보고 있는지 알 수 없게 된다.
    /// </summary>
    [RelayCommand]
    public void CollapseSearchIfEmpty()
    {
        if (!HasSearchText)
        {
            IsSearchExpanded = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RestartSearch();

    partial void OnIncludeSubfoldersChanged(bool value) => RestartSearch();

    partial void OnItemsChanged(IReadOnlyList<FileItemViewModel> value) => RestartSearch();

    /// <summary>
    /// 목록을 갈아끼우기 <b>직전</b>에 FPU 마스크를 되돌린다 (F-1 그물).
    /// </summary>
    /// <remarks>
    /// <c>DisplayItems</c> 대입이 곧 두 <c>ListView</c> 의 <c>ItemsSource</c> 통째 교체이고,
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
                buffer.Add(new FileItemViewModel(hit.Item, hit.RelativeFolder));

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
        Breadcrumbs = BuildBreadcrumbs(entry, path);
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
            Items = [.. listing.Items.Select(i => new FileItemViewModel(i))];

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

    private static IReadOnlyList<BreadcrumbSegment> BuildBreadcrumbs(FolderEntry entry, string currentPath)
    {
        var anchor = Normalize(entry.Path);
        var anchorName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? LastSegment(entry.Path)
            : entry.DisplayName!;

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
