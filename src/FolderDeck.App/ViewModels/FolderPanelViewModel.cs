using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        Action<string>? reportError = null)
    {
        _enumerator = enumerator;
        _shell = shell;
        _clipboard = clipboard;
        _reportError = reportError;
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
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    private FolderEntry? entry;

    /// <summary>지금 보고 있는 경로. <b>런타임 전용 — JSON 에 저장하지 않는다</b>(§4.6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAwayFromAnchor))]
    [NotifyPropertyChangedFor(nameof(LocationHint))]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    private string? currentPath;

    [ObservableProperty]
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
    private IReadOnlyList<FileItemViewModel> selectedItems = [];

    public bool HasSelection => SelectedItems.Count > 0;

    public string? SelectionText => HasSelection ? $"{SelectedItems.Count}개 선택" : null;

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

    /// <summary>헤더 메타 — 개수 · 뷰 방식 · 정렬 (§3.2).</summary>
    public string? MetaText
    {
        get
        {
            if (Entry is null)
            {
                return null;
            }

            var view = Entry.ViewMode == FolderViewMode.Details ? "details" : "list";
            var sort = Entry.SortBy switch
            {
                SortBy.Modified => "수정",
                SortBy.Size => "크기",
                SortBy.Type => "종류",
                _ => "이름",
            };

            return $"{DisplayItems.Count}개 · {view} · {sort}{(Entry.SortDesc ? " ↓" : " ↑")}";
        }
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

    // ── 우클릭 메뉴 (이번엔 두 항목만) ───────────────────────────────────────

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

    private void Report(string? error, string prefix)
    {
        if (error is not null)
        {
            _reportError?.Invoke($"{prefix} — {error}");
        }
    }

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

        try
        {
            var listing = await _enumerator
                .ListAsync(path, Entry.SortBy, Entry.SortDesc, token)
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
