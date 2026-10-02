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

public sealed partial class FolderPanelViewModel : ObservableObject
{

    private const int SearchFlushBatch = 32;
    private const int SearchFlushMs = 150;

    private const int RecursiveSearchDebounceMs = 250;

    private const int EverythingSearchDebounceMs = 250;

    private static readonly char[] Separators =
        [IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar];

    private readonly IFolderEnumerator _enumerator;
    private readonly IShellLauncher _shell;
    private readonly IClipboardService _clipboard;
    private readonly Action<string>? _reportError;

    private readonly Action<string>? _reportRejection;

    private CancellationTokenSource? _cts;

    private CancellationTokenSource? _searchCts;

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

        if (isSearchTile)
        {
            isSearchExpanded = true;
        }
    }

    private readonly record struct HistoryPosition(FolderEntry Entry, string Path);

    [ObservableProperty]
    private bool isRotating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    [NotifyPropertyChangedFor(nameof(ExpandTip))]
    private bool isExpanded;

    public string ExpandIcon => IsExpanded ? "⤡" : "⤢";

    public string ExpandTip => IsExpanded ? "원래 크기로 복원" : "타일을 화면 전체로 확장";

    public System.Windows.Input.ICommand? ToggleExpandCommand { get; set; }

    [ObservableProperty]
    private bool isSearchTile;

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

    public IReadOnlyDictionary<string, string> ExtensionGlyphs { get; set; } =
        new Dictionary<string, string>();

    public ShellIconService? ShellIconService { get; set; }

    public EverythingSearchService? EverythingSearchService { get; set; }

    public int EverythingMaxResults { get; set; } = AppSettings.DefaultEverythingMaxResults;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(HasEverythingStatusText))]
    private string? everythingStatusText;

    public bool HasEverythingStatusText => EverythingStatusText is not null;

    private bool everythingTruncated;

    private int everythingTotalCount;

    private SortBy searchSortBy = SortBy.Name;

    private bool searchSortDesc;

    private SortBy CurrentSortBy => IsSearchTile ? searchSortBy : Entry?.SortBy ?? SortBy.Name;

    private bool CurrentSortDesc => IsSearchTile ? searchSortDesc : Entry?.SortDesc == true;

    private bool searchShowPosition = true;

    private bool searchShowSize = true;

    private bool searchShowModified = true;

    private bool searchShowPreview;

    private double searchPreviewRatio = FolderEntry.DefaultPreviewRatio;

    public bool CurrentShowSize => IsSearchTile ? searchShowSize : Entry?.ShowSize ?? true;

    public bool CurrentShowModified => IsSearchTile ? searchShowModified : Entry?.ShowModified ?? true;

    public bool CurrentShowPreview => IsSearchTile ? searchShowPreview : Entry?.ShowPreview ?? false;

    public double CurrentPreviewRatio =>
        IsSearchTile ? searchPreviewRatio : Entry?.PreviewRatio ?? FolderEntry.DefaultPreviewRatio;

    public bool CurrentShowPosition => searchShowPosition;

    [ObservableProperty]
    private IReadOnlyList<FileItemViewModel> items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetaText))]
    [NotifyPropertyChangedFor(nameof(MetaCountText))]
    [NotifyPropertyChangedFor(nameof(ItemCountText))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private IReadOnlyList<FileItemViewModel> displayItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationHint))]
    private IReadOnlyList<BreadcrumbSegment> breadcrumbs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    [NotifyPropertyChangedFor(nameof(IsRecursiveSearch))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(NeedsLocationColumn))]
    private string searchText = string.Empty;

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

    [ObservableProperty]
    private bool isSearchExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    [NotifyPropertyChangedFor(nameof(SelectionSuffix))]
    [NotifyPropertyChangedFor(nameof(PreviewTarget))]
    private IReadOnlyList<FileItemViewModel> selectedItems = [];

    public bool HasSelection => SelectedItems.Count > 0;

    public string? SelectionText => HasSelection ? $"{SelectedItems.Count}개 선택" : null;

    public string SelectionSuffix => HasSelection ? $" · {SelectionText}" : string.Empty;

    public FileItemViewModel? PreviewTarget =>
        SelectedItems.Count == 1 && !SelectedItems[0].IsDirectory ? SelectedItems[0] : null;

    public void ClearSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        SelectedItems = [];
        SelectionCleared?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SelectionCleared;

    public event EventHandler? SelectionRestored;

    public FileDropPayload? BuildDragPayload(FileItemViewModel? pressedItem = null)
    {

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

        var sourceFolder = IsSearchTile ? SourceFolderFor(items[0]) : CurrentPath!;

        return new FileDropPayload(
            sourceFolder,
            [.. items.Select(i => new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))]);
    }

    private static string SourceFolderFor(FileItemViewModel item) =>
        IoPath.GetDirectoryName(item.FullPath) is { Length: > 0 } parent ? parent : item.FullPath;

    public System.Windows.Input.ICommand? TrashSelectionCommand { get; set; }

    public IShellDropHost? ShellDropHost { get; set; }

    public Action<int>? ReportExported { get; set; }

    public Action? SaveFolderEntry { get; set; }

    public System.Windows.Input.ICommand? RegisterFolderCommand { get; set; }

    public Func<FolderPanelViewModel, FileItemViewModel, bool>? SetAnchorFolder { get; set; }

    public Func<FolderPanelViewModel, string, bool>? SetAnchorFolderToPath { get; set; }

    public Action<string>? RegisterFolderPath { get; set; }

    public Func<string, string?>? PromptForNewFolderName { get; set; }

    public Func<FileItemViewModel, string?>? RenameOnDisk { get; set; }

    public Action<FolderPanelViewModel>? CompareRequested { get; set; }

    public Func<FolderPanelViewModel, Task>? BatchRenameRequested { get; set; }

    public Action<FolderPanelViewModel, FileItemViewModel>? CompareContentRequested { get; set; }

    public Func<string?, Task>? PasteInto { get; set; }

    [ObservableProperty]
    private bool isDropTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropTargetText))]
    private bool dropTargetCopies;

    public string DropTargetText => DropTargetCaption.For(DropTargetCopies);

    public string? ShellDropDestination => HasFailure ? null : CurrentPath;

    public bool IsEmpty => Entry is null && !IsSearchTile;

    public string AnchorName => Entry is null
        ? string.Empty
        : (string.IsNullOrWhiteSpace(Entry.DisplayName) ? LastSegment(Entry.Path) : Entry.DisplayName!);

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

    public bool IsDetailsView => IsSearchTile || Entry?.ViewMode == FolderViewMode.Details;

    public bool IsListView => !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.List;

    public bool IsLargeIconView =>
        !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.LargeIcons;

    public bool IsExtraLargeIconView =>
        !IsSearchTile && Entry is not null && Entry.ViewMode == FolderViewMode.ExtraLargeIcons;

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public bool IsRecursiveSearch => HasSearchText && IncludeSubfolders;

    public bool NeedsLocationColumn => IsRecursiveSearch || IsSearchTile;

    public bool ShowNoResults => HasSearchText && !IsSearching && DisplayItems.Count == 0 && EverythingStatusText is null;

    public bool HasSkippedFolders => SearchSkippedFolders > 0;

    public string SkippedFoldersText => $"{SearchSkippedFolders}개 폴더 건너뜀";

    public string? SearchStatusText => IsSearching
        ? $"검색 중… {DisplayItems.Count}건 · {SearchScannedFolders}개 폴더 탐색"
        : null;

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public string? MetaText =>
        Entry is null ? null : $"{MetaCountText}{ViewModeText}{MetaSortText}";

    public string MetaCountText => everythingTruncated
        ? $"{everythingTotalCount}개 중 {DisplayItems.Count}개 · "
        : $"{DisplayItems.Count}개 · ";

    public string ItemCountText => everythingTruncated
        ? $"{everythingTotalCount}개 중 {DisplayItems.Count}개"
        : $"{DisplayItems.Count}개";

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

    public string SortIcon => (FoldersFirst ? string.Empty : "∪") + (CurrentSortDesc ? "▾" : "▴");

    public string SortTip =>
        $"정렬: {SortLabel}{SortArrow}{(FoldersFirst ? string.Empty : " · 통합(폴더·파일 한 덩어리)")}"
        + " — 눌러서 기준 변경. 재귀 검색 중에는 잠긴다(결과가 찾은 순서대로 나온다).";

    public bool HasStatusLine => IsAwayFromAnchor || IsLoading;

    public string ViewModeText => Entry?.ViewMode switch
    {
        FolderViewMode.Details => "details",
        FolderViewMode.LargeIcons => "largeIcons",
        FolderViewMode.ExtraLargeIcons => "extraLargeIcons",
        _ => "list",
    };

    public string MetaSortText =>
        $" · {SortLabel}{SortArrow}{(FoldersFirst ? string.Empty : " · 통합")}";

    public string SortLabel => LabelOf(CurrentSortBy);

    public string SortArrow => CurrentSortDesc ? " ↓" : " ↑";

    public string NameHeaderText => HeaderTextFor(SortBy.Name, "이름");

    public string SizeHeaderText => HeaderTextFor(SortBy.Size, "크기");

    public string ModifiedHeaderText => HeaderTextFor(SortBy.Modified, "수정");

    public bool FoldersFirst => Entry?.FoldersFirst ?? true;

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SetSortAsync(SortBy sort)
    {
        if (IsSearchTile)
        {

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

    public bool IsAwayFromAnchor =>
        Entry is not null && CurrentPath is not null && !PathEquals(CurrentPath, Normalize(Entry.Path));

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task GoToAnchorAsync() =>
        Entry is null ? Task.CompletedTask : NavigateAsync(Entry, Normalize(Entry.Path), pushHistory: true);

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task RefreshAsync() =>
        CurrentPath is null ? Task.CompletedTask : LoadAsync(CurrentPath);

    public event EventHandler? LoadCompleted;

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

        Report(_shell.Open(item.FullPath), $"열 수 없다: {item.Name}");
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void CopyItemPath(FileItemViewModel? item)
    {
        if (item is not null)
        {
            Report(_clipboard.SetText(item.FullPath), "경로를 복사하지 못했다");
        }
    }

    [RelayCommand]
    public void CopyCurrentPath()
    {
        if (CurrentPath is not null)
        {
            Report(_clipboard.SetText(CurrentPath), "경로를 복사하지 못했다");
        }
    }

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

    [RelayCommand]
    public void ShowItemProperties(FileItemViewModel? item)
    {
        if (item is not null)
        {
            Report(_shell.ShowProperties(item.FullPath), $"속성을 열지 못했다: {item.Name}");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SetAnchorToItemAsync(FileItemViewModel? item)
    {

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

    public static string SuggestNewFolderName(string parentPath)
    {
        const string baseName = "새 폴더";
        var candidate = IoPath.Combine(parentPath, baseName);

        return IoDirectory.Exists(candidate) || System.IO.File.Exists(candidate)
            ? IoPath.GetFileName(FileOperationEngine.PreviewUniqueName(candidate))
            : baseName;
    }

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

    }

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

            return;
        }

        CreateNewFolder(typed);
    }

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

        if (DisplayItems.FirstOrDefault(i => PathEquals(i.FullPath, renamed)) is { } moved)
        {
            SelectedItems = [moved];
            SelectionRestored?.Invoke(this, EventArgs.Empty);
        }
    }

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

    [RelayCommand]
    private void StartCompare() => CompareRequested?.Invoke(this);

    [RelayCommand]
    private void CompareContent(FileItemViewModel item) => CompareContentRequested?.Invoke(this, item);

    [RelayCommand]
    private Task BatchRenameSelection()
    {
        if (SelectedItems.Count == 0 || BatchRenameRequested is null)
        {
            return Task.CompletedTask;
        }

        return BatchRenameRequested(this);
    }

    [RelayCommand]
    public void CopySelectionToClipboard()
    {
        if (SelectedItems.Count == 0)
        {

            Reject("복사할 것이 없다 — 먼저 고른 뒤에 Ctrl+C");
            return;
        }

        Report(
            _clipboard.SetFiles(
                [.. SelectedItems.Select(i =>
                    new Core.Operations.OperationItem(i.FullPath, i.IsDirectory))]),
            "클립보드에 올리지 못했다");
    }

    [RelayCommand]
    public void CutSelectionToClipboard()
    {
        if (SelectedItems.Count == 0)
        {

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task PasteIntoCurrentAsync() =>
        PasteInto is { } paste ? paste(ShellDropDestination) : Task.CompletedTask;

    [RelayCommand]
    public void ExpandSearch()
    {
        if (IsSearchTile)
        {
            return;
        }

        IsSearchExpanded = true;
    }

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

    partial void OnDisplayItemsChanged(IReadOnlyList<FileItemViewModel> value)
    {
        using var fpu = FpuGuard.Enter("그물:DisplayItems");
    }

    private void RestartSearch()
    {
        CancelSearch();

        var query = SearchText;

        if (IsSearchTile)
        {
            RestartEverythingSearch(query);
            return;
        }

        if (string.IsNullOrEmpty(query))
        {
            SearchScannedFolders = 0;
            SearchSkippedFolders = 0;
            DisplayItems = Items;
            return;
        }

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

        IsSearching = true;
        _ = SearchRecursivelyAsync(CurrentPath, query, cts);
    }

    private async Task SearchRecursivelyAsync(string root, string query, CancellationTokenSource cts)
    {
        var token = cts.Token;

        try
        {

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

        }
        catch (Exception ex)
        {
            _reportError?.Invoke($"검색 실패: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {

                _searchCts = null;
                IsSearching = false;
                SearchScannedFolders = stats.FoldersScanned;
                SearchSkippedFolders = stats.FoldersSkipped;
                OnPropertyChanged(nameof(ShowNoResults));
            }

            await cts.CancelAsync().ConfigureAwait(true);
            await progress.ConfigureAwait(true);
            cts.Dispose();
        }
    }

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

            return;
        }

        _searchCts = null;

        if (result.Outcome != EverythingSearchOutcome.Ok)
        {
            EverythingStatusText = DescribeOutcome(result.Outcome);
            DisplayItems = [];
            return;
        }

        everythingTruncated = result.Truncated;
        everythingTotalCount = result.TotalCount;

        DisplayItems = [.. result.Items.Select(item => new FileItemViewModel(
            item,
            IoPath.GetDirectoryName(item.FullPath) ?? string.Empty,
            ExtensionGlyphs,
            ShellIconService))];
    }

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

        }

        if (IsSearching)
        {
            IsSearching = false;
        }
    }

    private async Task NavigateAsync(FolderEntry entry, string path, bool pushHistory)
    {

        var samePlace = ReferenceEquals(Entry, entry)
                        && CurrentPath is not null
                        && PathEquals(CurrentPath, path);

        if (pushHistory && !samePlace)
        {
            PushCurrentTo(_back);
            _forward.Clear();
            NotifyHistoryChanged();
        }

        CancelSearch();
        SearchText = string.Empty;

        ClearSelection();

        Entry = entry;

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

    private void Reject(string text) => (_reportRejection ?? _reportError)?.Invoke(text);

    private void Report(string? error, string prefix)
    {
        if (error is not null)
        {
            _reportError?.Invoke($"{prefix} — {error}");
        }
    }

    public void ReportShellMenuFailure(string message) => _reportError?.Invoke(message);

    public void ReportShellMenuRejection(string message) => Reject(message);

    private async Task LoadAsync(string path)
    {
        if (Entry is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);

        var token = cts.Token;

        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {

        }

        IsLoading = true;

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

            var chosen = SelectedItems.Count == 0
                ? null
                : SelectedItems.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Failure = listing.Failure;
            Items = [.. listing.Items.Select(i =>
                new FileItemViewModel(i, extensionGlyphs: ExtensionGlyphs, shellIconService: ShellIconService))];

            if (chosen is not null)
            {

                SelectedItems = [.. Items.Where(i => chosen.Contains(i.FullPath))];
                SelectionRestored?.Invoke(this, EventArgs.Empty);
            }

            completed = true;
        }
        catch (OperationCanceledException)
        {

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

        if (IsUnder(currentPath, anchor))
        {
            var segments = new List<BreadcrumbSegment> { new(anchorName, anchor, true) };
            Accumulate(segments, anchor, currentPath[anchor.Length..]);
            return segments;
        }

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
