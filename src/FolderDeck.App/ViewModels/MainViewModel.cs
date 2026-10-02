using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.App.Views;
using FolderDeck.App.Watching;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Paths;
using FolderDeck.Core.Renaming;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.ViewModels;


public sealed partial class MainViewModel : ObservableObject, IShellDropHost
{
    private readonly IWorkspaceStore _store;
    private readonly IFolderEnumerator _enumerator;
    private readonly IShellLauncher _shell;
    private readonly IClipboardService _clipboard;
    private readonly IFileOperationEngine _engine;
    private readonly IUserPrompt _prompt;
    private readonly IMacroEditor _macroEditor;
    private readonly IFolderEditor _folderEditor;
    private readonly ISettingsEditor _settingsEditor;
    private readonly ISelfLauncher _selfLauncher;


    private readonly AppSettings _settings;







    private readonly ShellIconService _shellIconService = new();







    private EverythingSearchService? _everythingSearchService;


    private readonly bool _settingsWritable;


    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;


    private readonly Dictionary<TileViewModel, FolderWatcher> _watchers = [];

    public MainViewModel(
        Workspace workspace,
        IWorkspaceStore store,
        IFolderEnumerator enumerator,
        IShellLauncher shell,
        IClipboardService clipboard,
        IFileOperationEngine engine,
        IUserPrompt prompt,
        IMacroEditor macroEditor,
        IFolderEditor folderEditor,
        ISelfLauncher selfLauncher,
        AppSettings? settings = null,
        bool settingsWritable = true,
        ISettingsEditor? settingsEditor = null)
    {
        Workspace = workspace;
        _store = store;
        _enumerator = enumerator;
        _shell = shell;
        _clipboard = clipboard;
        _engine = engine;
        _prompt = prompt;
        _macroEditor = macroEditor;
        _folderEditor = folderEditor;
        _selfLauncher = selfLauncher;


        _settingsEditor = settingsEditor ?? NullSettingsEditor.Instance;


        _settings = settings ?? new AppSettings();
        _settingsWritable = settingsWritable;
        tileGap = _settings.TileGap;

        Rows = [.. workspace.Folders.Select(f => new FolderRowViewModel(f))];



        var grid = GridLayout.EnsureGrid(workspace);
        GridCols = grid.Cols;
        GridRows = grid.Rows;

        var derived = GridLayout.EnsureTiles(workspace);
        var dropped = GridLayout.NormalizeTiles(workspace);

        Tiles = [.. workspace.Tiles!.Select(CreateTile)];

        Cells = [.. Enumerable.Range(0, GridRows)
            .SelectMany(y => Enumerable.Range(0, GridCols).Select(x => new GridCellViewModel(x, y)))];



        Tray = [.. (workspace.CopyTray ?? [])
            .Select(id => Rows.FirstOrDefault(r => r.Entry.Id == id))
            .Where(r => r is not null)
            .Select(r => new TrayEntryViewModel(r!.Entry, RemoveFromTray))];

        Macros = [.. (workspace.Macros ?? []).Select(m => new MacroCardViewModel(m))];

        RefreshDerivedLayout();

        if (derived || dropped > 0)
        {
            _store.SaveWorkspaceDebounced(workspace);
        }

        if (dropped > 0)
        {

            Warn($"배치에서 놓을 수 없는 타일 {dropped}개를 뺐다 (폴더가 없거나 격자를 벗어났다).");
        }

        _store.SaveFailed += OnSaveFailed;
    }

    public Workspace Workspace { get; }

    public string Title => string.IsNullOrWhiteSpace(Workspace.Title) ? "(제목 없음)" : Workspace.Title;

















    public string WindowTitle => string.IsNullOrWhiteSpace(Workspace.Description)
        ? $"FolderDeck — {Title}"
        : $"FolderDeck — {Title} · {Workspace.Description!.Trim()}";

    public ObservableCollection<FolderRowViewModel> Rows { get; }















    public ObservableCollection<FolderRowViewModel> RotatingRows { get; } = [];

    public int GridCols { get; }

    public int GridRows { get; }


    public ObservableCollection<TileViewModel> Tiles { get; }


    public ObservableCollection<GridCellViewModel> Cells { get; }


    public IReadOnlyList<FolderPanelViewModel> Panels { get; private set; } = [];


    public FolderPanelViewModel? RotatingPanel => PrimaryRotatingTile?.Panel;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusPath))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private TileViewModel? activeRotatingTile;


    public string StatusPath =>
        (ActiveRotatingTile ?? PrimaryRotatingTile)?.Panel.CurrentPath
        ?? "왼쪽 목록에서 폴더를 클릭하면 여기에 전체 경로가 나온다.";


    public string StatusLabel
    {
        get
        {
            var tile = ActiveRotatingTile ?? PrimaryRotatingTile;
            return tile is null ? "순환 칸:" : $"{tile.KindTag} 칸:";
        }
    }











    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? message;


    [ObservableProperty]
    private bool messageIsWarning;


    [ObservableProperty]
    private bool messageIsTransient;

    public bool HasMessage => !string.IsNullOrEmpty(Message);


    private void Inform(string text) => SetMessage(text, warning: false, transient: true);





    private void Warn(string text, bool transient = false) =>
        SetMessage(text, warning: true, transient);

    private void ClearMessage() => SetMessage(null, warning: false, transient: false);













    private void SetMessage(string? text, bool warning, bool transient)
    {
        MessageIsWarning = warning;
        MessageIsTransient = transient;
        Message = text;
    }


    public void ExpireTransientMessage()
    {
        if (MessageIsTransient)
        {
            ClearMessage();
        }
    }


    public void ReportTileOverlap() => Warn("그 자리에는 다른 타일이 있다.", transient: true);


    public void RejectDuplicatePlacement(FolderRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Warn($"이미 상시 칸에 있다: {row.DisplayName}", transient: true);
    }







    public void ReportDuplicateWindow() => Warn(
        "이미 열려 있는 창에 신호를 보내지 못해 이 창을 새로 열었다. " +
        "같은 작업 관리가 두 창에 떠 있을 수 있다 — 한쪽을 닫는 편이 좋다.");




    public void ReportNoSignal(string detail) => Warn(
        $"이 창은 '창 보이기' 신호를 받지 못한다 (파이프를 열지 못했다: {detail}). " +
        "이 작업 관리를 다시 열면 창이 하나 더 뜬다.");










    [RelayCommand]
    public void OpenLauncher()
    {
        var error = _selfLauncher.OpenLauncher();
        if (error is not null)
        {
            Warn($"선택 창을 띄우지 못했다: {error}");
            return;
        }

        Inform("선택 창을 띄웠다.");
    }


    public bool CanOpenSettings => _settingsWritable;













    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        var request = new SettingsEditRequest(
            _settings.ExtensionGlyphs, _settings.ShowShellIcons, _settings.EverythingMaxResults,
            EverythingProbe.Detect());

        if (_settingsEditor.Edit(request) is not { } updated)
        {
            return;
        }

        _settings.ExtensionGlyphs = new Dictionary<string, string>(updated.ExtensionGlyphs);
        _settings.ShowShellIcons = updated.ShowShellIcons;
        _settings.EverythingMaxResults = updated.EverythingMaxResults;

        foreach (var panel in Panels)
        {
            panel.ExtensionGlyphs = _settings.ExtensionGlyphs;
            panel.ShellIconService = _settings.ShowShellIcons ? _shellIconService : null;



            panel.EverythingMaxResults = _settings.EverythingMaxResults;
        }

        if (_settingsWritable)
        {
            _store.SaveSettingsDebounced(_settings);
        }

        await RefreshAllAsync().ConfigureAwait(true);
    }











    public void UpdateWindowPlacement(ScreenRect bounds, bool maximized)
    {
        if (!bounds.IsUsable)
        {

            return;
        }

        var window = Workspace.Window;
        if (window is not null
            && window.X == bounds.X && window.Y == bounds.Y
            && window.Width == bounds.Width && window.Height == bounds.Height
            && window.Maximized == maximized)
        {
            return;
        }

        window ??= Workspace.Window = new WindowSpec();
        window.X = bounds.X;
        window.Y = bounds.Y;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Maximized = maximized;

        Save();
    }





    public void ReportWindowMoved() =>
        Inform("저장해 둔 창 자리를 지금 화면에서 되찾을 수 없어 기본 위치로 띄웠다.");





    public void ReportWindowAtMinimum() =>
        Inform("지금 화면이 좁아 창을 최소 크기로 띄웠다 — 화면 밖으로 조금 넘칠 수 있다.");


    public void ReportWindowResized() =>
        Inform("저장해 둔 창 크기가 지금 화면보다 커서 화면에 맞춰 줄였다.");

    private TileViewModel? PrimaryRotatingTile => Tiles
        .Where(t => t.IsRotating)
        .OrderBy(t => t.RotationIndex ?? int.MaxValue)
        .FirstOrDefault();

    private IEnumerable<TileSpec> Specs => Tiles.Select(t => t.Spec);







    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewMode))]
    [NotifyPropertyChangedFor(nameof(EditToggleText))]
    private bool isEditingLayout;


    public bool IsViewMode => !IsEditingLayout;

    public string EditToggleText => IsEditingLayout ? "배치 편집 끝내기" : "배치 편집";


    public IReadOnlyList<int> TileGapChoices => AppSettings.TileGapChoices;











    [ObservableProperty]
    private int tileGap = AppSettings.DefaultTileGap;





    partial void OnTileGapChanged(int value)
    {

        _settings.TileGap = value;

        if (!_settingsWritable)
        {
            return;
        }

        _store.SaveSettingsDebounced(_settings);
    }

    [RelayCommand(CanExecute = nameof(CanToggleLayoutEdit))]
    public void ToggleLayoutEdit()
    {
        IsEditingLayout = !IsEditingLayout;
        CancelPlace();


        ClearMessage();

        if (!IsEditingLayout)
        {
            Save();
        }
    }






    private bool CanToggleLayoutEdit() => !IsAnyTileExpanded;

    partial void OnIsEditingLayoutChanged(bool value)
    {
        foreach (var tile in Tiles)
        {
            tile.IsEditing = value;
            tile.IsDragging = false;
        }
    }








    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyTileExpanded))]
    [NotifyCanExecuteChangedFor(nameof(ToggleLayoutEditCommand))]
    private TileViewModel? expandedTile;

    public bool IsAnyTileExpanded => ExpandedTile is not null;





    partial void OnExpandedTileChanged(TileViewModel? value)
    {
        foreach (var tile in Tiles)
        {
            tile.IsExpanded = ReferenceEquals(tile, value);
            tile.IsHiddenByExpansion = value is not null && !ReferenceEquals(tile, value);
            tile.ExpandedSpanX = GridCols;
            tile.ExpandedSpanY = GridRows;
        }
    }





    [RelayCommand]
    private void ToggleExpand(FolderPanelViewModel? panel)
    {
        if (panel is null)
        {
            return;
        }

        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Panel, panel));
        if (tile is null)
        {
            return;
        }

        ExpandedTile = ReferenceEquals(ExpandedTile, tile) ? null : tile;
    }




    public bool CanPlaceTile(TileViewModel? tile, int cellX, int cellY, int spanX, int spanY) =>
        GridLayout.CanPlace(Specs, tile?.Spec, cellX, cellY, spanX, spanY, GridCols, GridRows);


    public bool MoveTile(TileViewModel tile, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (!IsEditingLayout || !CanPlaceTile(tile, cellX, cellY, tile.SpanX, tile.SpanY))
        {
            return false;
        }

        if (tile.CellX == cellX && tile.CellY == cellY)
        {
            return true;
        }

        tile.Spec.CellX = cellX;
        tile.Spec.CellY = cellY;
        CommitPlacement(tile);
        return true;
    }


    public bool ResizeTile(TileViewModel tile, int spanX, int spanY)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (!IsEditingLayout || !CanPlaceTile(tile, tile.CellX, tile.CellY, spanX, spanY))
        {
            return false;
        }

        if (tile.SpanX == spanX && tile.SpanY == spanY)
        {
            return true;
        }

        tile.Spec.SpanX = spanX;
        tile.Spec.SpanY = spanY;
        CommitPlacement(tile);
        return true;
    }





    [RelayCommand]
    public void RemoveTile(TileViewModel? tile)
    {



        if (tile is null || !Tiles.Contains(tile) || !IsEditingLayout)
        {
            return;
        }





        if (ReferenceEquals(ExpandedTile, tile))
        {
            ExpandedTile = null;
        }

        var removedIndex = tile.IsRotating ? tile.RotationIndex : null;

        Workspace.Tiles!.Remove(tile.Spec);
        Tiles.Remove(tile);
        tile.Panel.Clear();
        DetachWatcher(tile);



        if (ReferenceEquals(tile, ComparePeerA) || ReferenceEquals(tile, ComparePeerB))
        {
            ClearCompare();
        }

        var orphans = removedIndex is null ? 0 : DemoteFoldersTo(removedIndex.Value, 1);

        RefreshDerivedLayout();
        Save();

        Inform(removedIndex is null
            ? "타일을 뺐다. 폴더는 왼쪽 리스트에 남아 있다."
            : orphans > 0
                ? $"순환{removedIndex} 칸을 뺐다 — 그 번호를 쓰던 폴더 {orphans}개는 순환1로."
                : $"순환{removedIndex} 칸을 뺐다.");
    }





    [RelayCommand]
    public void ToggleTileKind(TileViewModel? tile)
    {
        if (tile is null || !Tiles.Contains(tile))
        {
            return;
        }

        if (tile.IsRotating)
        {
            if (tile.Panel.Entry is not { } entry)
            {
                Warn($"{tile.KindTag} 칸이 비어 있어 못박을 폴더가 없다.", transient: true);
                return;
            }

            var freed = tile.RotationIndex ?? 1;
            tile.Spec.Kind = TileKind.Pinned;
            tile.Spec.FolderId = entry.Id;
            tile.Spec.RotationIndex = null;

            var orphans = DemoteFoldersTo(freed, 1);
            Inform(orphans > 0
                ? $"순환{freed} → 상시. 그 번호를 쓰던 폴더 {orphans}개는 순환1로."
                : "이 칸을 상시로 바꿨다.");
        }
        else
        {
            tile.Spec.Kind = TileKind.Rotating;
            tile.Spec.FolderId = null;
            tile.Spec.RotationIndex = GridLayout.NextRotationIndex(Specs.Where(s => s != tile.Spec));
            tile.Panel.Clear();
            Inform($"이 칸을 순환{tile.Spec.RotationIndex} 으로 바꿨다.");
        }

        tile.NotifyKind();
        RefreshDerivedLayout();
        Save();
    }




    [ObservableProperty]
    private bool isFolderPickerOpen;


    [ObservableProperty]
    private GridCellViewModel? pendingCell;


    [RelayCommand]
    public void BeginPlaceInCell(GridCellViewModel? cell)
    {
        if (!IsEditingLayout || cell is null || !cell.IsFree)
        {
            return;
        }

        PendingCell = cell;
        IsFolderPickerOpen = true;
    }

    [RelayCommand]
    public void CancelPlace()
    {
        IsFolderPickerOpen = false;
        PendingCell = null;

        foreach (var cell in Cells)
        {
            cell.IsDropTarget = false;
        }
    }


    [RelayCommand]
    public async Task PlaceFolderAsync(FolderRowViewModel? row)
    {
        var cell = PendingCell;
        CancelPlace();

        if (row is not null && cell is not null)
        {
            await PlaceFolderInCellAsync(row, cell).ConfigureAwait(true);
        }
    }


    [RelayCommand]
    public void PlaceRotating()
    {
        var cell = PendingCell;
        CancelPlace();

        if (cell is not null)
        {
            PlaceRotatingInCell(cell);
        }
    }



    public bool HasSearchTile => Tiles.Any(t => t.Spec.Kind == TileKind.Search);


    [RelayCommand]
    public void PlaceSearch()
    {
        var cell = PendingCell;
        CancelPlace();

        if (cell is not null)
        {
            PlaceSearchInCell(cell);
        }
    }


    public async Task<bool> PlaceFolderInCellAsync(FolderRowViewModel row, GridCellViewModel cell)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cell);

        if (!IsEditingLayout || !CanPlaceTile(null, cell.X, cell.Y, 1, 1))
        {
            return false;
        }



        if (PinnedTileFor(row) is not null)
        {
            RejectDuplicatePlacement(row);
            return false;
        }

        var spec = new TileSpec
        {
            Kind = TileKind.Pinned,
            FolderId = row.Entry.Id,
            CellX = cell.X,
            CellY = cell.Y,
            SpanX = 1,
            SpanY = 1,
        };

        var tile = AddTile(spec);
        Inform($"상시 칸에 넣었다: {row.DisplayName}");




        await tile.Panel.GoToAnchorAsync().ConfigureAwait(true);
        UpdateRowStates();
        return true;
    }

    public bool PlaceRotatingInCell(GridCellViewModel cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (!IsEditingLayout || !CanPlaceTile(null, cell.X, cell.Y, 1, 1))
        {
            return false;
        }

        var spec = new TileSpec
        {
            Kind = TileKind.Rotating,
            CellX = cell.X,
            CellY = cell.Y,
            SpanX = 1,
            SpanY = 1,
            RotationIndex = GridLayout.NextRotationIndex(Specs),
        };

        AddTile(spec);
        Inform($"순환{spec.RotationIndex} 칸을 만들었다 — 폴더 행 우클릭으로 이 번호를 지정한다.");
        return true;
    }

    public bool PlaceSearchInCell(GridCellViewModel cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (!IsEditingLayout || !CanPlaceTile(null, cell.X, cell.Y, 1, 1) || HasSearchTile)
        {
            return false;
        }

        var spec = new TileSpec
        {
            Kind = TileKind.Search,
            CellX = cell.X,
            CellY = cell.Y,
            SpanX = 1,
            SpanY = 1,
        };

        AddTile(spec);
        Inform("이 칸을 전역 검색으로 만들었다.");
        return true;
    }



    private void ApplyRotationChoice(RotationChoiceViewModel choice)
    {

        choice.Row.Entry.RotationSlot = choice.Slot <= 1 ? null : choice.Slot;
        choice.Row.NotifyRotationSlot();

        RefreshRotationChoices();
        Save();

        Inform($"{choice.Row.DisplayName} → {choice.Label} 칸");
    }




    public ObservableCollection<TrayEntryViewModel> Tray { get; }

    public bool TrayIsEmpty => Tray.Count == 0;





    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(OperationSummary))]
    [NotifyPropertyChangedFor(nameof(CanRunOperation))]
    private FolderPanelViewModel? selectionOwner;

    public bool HasSelection => SelectionOwner?.HasSelection == true;

    private IReadOnlyList<FileItemViewModel> Selection => SelectionOwner?.SelectedItems ?? [];

    private List<TrayEntryViewModel> CheckedDestinations => [.. Tray.Where(t => t.IsChecked)];


    public string OperationSummary
    {
        get
        {
            var sources = Selection.Count;
            var destinations = CheckedDestinations.Count;

            if (sources == 0)
            {
                return "그리드에서 파일을 고르면 여기에 요약이 나온다.";
            }

            if (destinations == 0)
            {
                return $"파일 {sources}개 선택 — 대상함에서 목적지를 체크하라.";
            }

            var text = $"파일 {sources}개 → 폴더 {destinations}개 = {sources * destinations}건";


            return destinations > 1 ? text + " · 이동은 목적지 하나만" : text;
        }
    }

    public bool CanRunOperation => HasSelection && CheckedDestinations.Count > 0 && !IsOperationRunning;


    [RelayCommand]
    public void AddToTray(FolderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (Tray.Any(t => t.Entry.Id == row.Entry.Id))
        {
            Warn($"대상함에 이미 있다: {row.DisplayName}", transient: true);
            return;
        }

        Tray.Add(new TrayEntryViewModel(row.Entry, RemoveFromTray));
        SaveTray();
        Inform($"대상함에 넣었다: {row.DisplayName}");
    }

    private void RemoveFromTray(TrayEntryViewModel entry)
    {
        if (Tray.Remove(entry))
        {
            SaveTray();
            Inform($"대상함에서 뺐다: {entry.DisplayName}");
        }
    }

    private void SaveTray()
    {

        Workspace.CopyTray = Tray.Count == 0 ? null : [.. Tray.Select(t => t.Entry.Id)];
        OnPropertyChanged(nameof(TrayIsEmpty));
        NotifyOperationState();
        Save();
    }


    public void NotifyOperationState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(OperationSummary));
        OnPropertyChanged(nameof(CanRunOperation));
        OnPropertyChanged(nameof(CanTrashSelection));
        OnPropertyChanged(nameof(CanCreateMacro));
        CopySelectionCommand.NotifyCanExecuteChanged();
        MoveSelectionCommand.NotifyCanExecuteChanged();
        TrashSelectionCommand.NotifyCanExecuteChanged();
        CreateMacroCommand.NotifyCanExecuteChanged();
        EditMacroCommand.NotifyCanExecuteChanged();
    }


    public void OnPanelSelectionChanged(FolderPanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (panel.HasSelection)
        {
            foreach (var other in Panels.Where(p => !ReferenceEquals(p, panel)))
            {
                other.ClearSelection();
            }

            SelectionOwner = panel;
        }
        else if (ReferenceEquals(SelectionOwner, panel))
        {
            SelectionOwner = null;
        }

        NotifyOperationState();
    }

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    public Task CopySelectionAsync() => RunOperationAsync(FileOperationKind.Copy);

    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    public Task MoveSelectionAsync() => RunOperationAsync(FileOperationKind.Move);







    public bool IsOperationRunning
    {
        get => _isOperationRunning;
        private set
        {
            if (SetProperty(ref _isOperationRunning, value))
            {
                NotifyOperationState();
            }
        }
    }

    private bool _isOperationRunning;

    private Task RunOperationAsync(FileOperationKind op)
    {
        var sources = Selection;
        var destinations = CheckedDestinations;

        if (sources.Count == 0 || destinations.Count == 0)
        {
            return Task.CompletedTask;
        }




        if (op == FileOperationKind.Move && destinations.Count > 1)
        {
            Warn($"이동은 목적지 하나만 고를 수 있다 (지금 {destinations.Count}개). " +
                 "여러 곳에 두려면 복사를 쓴다.");
            return Task.CompletedTask;
        }

        return StartOperationAsync(
            op,
            [.. sources.Select(i => new OperationItem(i.FullPath, i.IsDirectory))],
            [.. destinations.Select(t => t.Entry.Path)],
            confirmMove: true);
    }

















    private async Task StartOperationAsync(
        FileOperationKind op,
        IReadOnlyList<OperationItem> items,
        IReadOnlyList<string> destinations,
        bool confirmMove,
        int sameFolderSkipped = 0,
        string? verbOverride = null)
    {
        var verb = verbOverride ?? (op == FileOperationKind.Copy ? "복사" : "이동");



        IsOperationRunning = true;

        FileOperationPreview preview;
        try
        {


            preview = await _engine
                .PreviewAsync(new FileOperationRequest(op, items, destinations, ConflictPolicy.Overwrite))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {


            IsOperationRunning = false;
            Warn($"{verb} 준비 중 오류가 났다: {ex.Message}");
            return;
        }

        if (preview.TotalItemCount == 0)
        {

            IsOperationRunning = false;
            Warn($"{verb}할 곳이 없다 — 목적지에 닿지 못했다.");
            return;
        }

        var policy = ConflictPolicy;

        if (preview.TotalOverwriteCount > 0 && AskOnConflict)
        {


            var decision = _prompt.AskConflict(
                $"같은 이름 {preview.TotalOverwriteCount}건이 이미 있다",
                ConflictMessage(op, preview, items.Count),
                policy);

            if (decision is null)
            {
                IsOperationRunning = false;
                Inform($"{verb}를 취소했다.");
                return;
            }

            policy = decision.Policy;

            if (decision.Remember)
            {
                RememberConflictPolicy(policy);
            }
        }
        else if (op == FileOperationKind.Move && confirmMove
                 && !_prompt.Confirm($"{verb} 확인", MoveWarning(preview, items.Count, policy)))
        {

            IsOperationRunning = false;
            Inform("이동을 취소했다.");
            return;
        }

        await RunGuardedAsync(
            new FileOperationRequest(op, items, destinations, policy),
            verb,
            sameFolderSkipped: sameFolderSkipped).ConfigureAwait(true);
    }





    private async Task RunGuardedAsync(
        FileOperationRequest request,
        string verb,
        string? successMessage = null,
        int sameFolderSkipped = 0)
    {
        IsOperationRunning = true;

        try
        {
            await ExecuteRequestAsync(request, verb, successMessage, sameFolderSkipped)
                .ConfigureAwait(true);
        }
        finally
        {

            await RefreshAllPanelsAsync().ConfigureAwait(true);
            IsOperationRunning = false;
        }
    }

    private async Task ExecuteRequestAsync(
        FileOperationRequest request, string verb, string? successMessage, int sameFolderSkipped)
    {
        using var cts = new CancellationTokenSource();
        _operationCts = cts;

        ProgressValue = 0;
        ProgressText = $"{verb} 준비 중…";
        _progressRevealPending = true;
        _ = RevealProgressSoonAsync();

        FileOperationReport report;

        try
        {
            var progress = new ThrottledProgress(_uiContext, p => ApplyProgress(verb, p));
            report = await _engine.ExecuteAsync(request, progress, cts.Token).ConfigureAwait(true);
        }
        finally
        {


            _progressRevealPending = false;




            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts = null;
            }

            IsProgressVisible = false;
        }

        ReportOperation(verb, report, successMessage, sameFolderSkipped);
    }







    public ObservableCollection<MacroCardViewModel> Macros { get; }

    public bool MacrosAreEmpty => Macros.Count == 0;


    public bool CanCreateMacro => HasSelection && !IsOperationRunning;





    [RelayCommand(CanExecute = nameof(CanCreateMacro))]
    public void CreateMacro()
    {
        var selection = Selection;

        if (selection.Count == 0)
        {
            return;
        }

        var checkedTray = CheckedDestinations;

        var suggested = new MacroDraft(
            Name: SuggestMacroName(selection.Count, checkedTray),
            Op: FileOperationKind.Copy,
            SourceKind: MacroSourceKind.Selection,
            FixedPath: selection.Count == 1 ? selection[0].FullPath : null,
            DestKind: MacroDestKind.Tray,
            OnConflict: ConflictPolicy,
            Confirm: true);

        if (_macroEditor.Edit(suggested, canFixPath: selection.Count == 1, isEdit: false) is not { } draft)
        {
            return;
        }

        var definition = new MacroDefinition
        {
            Id = Guid.NewGuid(),
            Name = draft.Name,
            Op = draft.Op,
            Source = new MacroSource
            {
                Kind = draft.SourceKind,
                Path = draft.SourceKind == MacroSourceKind.FixedPath ? draft.FixedPath : null,
            },
            OnConflict = draft.OnConflict,


            Confirm = draft.Confirm || draft.Op != FileOperationKind.Copy,
        };

        if (draft.Op != FileOperationKind.Trash)
        {
            definition.Dest = new MacroDest
            {
                Kind = draft.DestKind,



                FolderIds = draft.DestKind == MacroDestKind.FolderIds
                    ? [.. checkedTray.Select(t => t.Entry.Id)]
                    : null,
            };
        }

        Workspace.Macros ??= [];
        Workspace.Macros.Add(definition);
        Macros.Add(new MacroCardViewModel(definition));

        NotifyMacros();
        Save();
        Inform($"매크로를 만들었다: {definition.Name}");
    }


    public bool CanEditMacro => !IsOperationRunning;





    [RelayCommand(CanExecute = nameof(CanEditMacro))]
    public void EditMacro(MacroCardViewModel? card)
    {
        if (card is null || !Macros.Contains(card))
        {
            return;
        }

        var definition = card.Definition;
        var selection = Selection;



        var hasFixedPath = definition.Source?.Kind == MacroSourceKind.FixedPath;
        var canFixPath = hasFixedPath || selection.Count == 1;
        var fixedPath = hasFixedPath
            ? definition.Source!.Path
            : selection.Count == 1 ? selection[0].FullPath : null;

        var originalDestKind = definition.Dest?.Kind;

        var draft = new MacroDraft(
            Name: definition.Name ?? string.Empty,
            Op: definition.Op,
            SourceKind: definition.Source?.Kind ?? MacroSourceKind.Selection,
            FixedPath: fixedPath,
            DestKind: originalDestKind ?? MacroDestKind.Tray,
            OnConflict: definition.OnConflict,
            Confirm: definition.Confirm);

        if (_macroEditor.Edit(draft, canFixPath, isEdit: true) is not { } updated)
        {
            return;
        }





        var resolvedFixedPath = updated.SourceKind != MacroSourceKind.FixedPath
            ? null
            : hasFixedPath && !updated.RetargetFixedPath
                ? definition.Source!.Path
                : selection.Count == 1 ? selection[0].FullPath : definition.Source?.Path;

        definition.Name = updated.Name;
        definition.Op = updated.Op;
        definition.Source = new MacroSource
        {
            Kind = updated.SourceKind,
            Path = resolvedFixedPath,
        };
        definition.OnConflict = updated.OnConflict;
        definition.Confirm = updated.Confirm || updated.Op != FileOperationKind.Copy;

        if (updated.Op != FileOperationKind.Trash)
        {






            var folderIds = updated.DestKind == MacroDestKind.FolderIds
                ? (originalDestKind == MacroDestKind.FolderIds && !updated.RepinFolderIds
                    ? definition.Dest!.FolderIds
                    : [.. CheckedDestinations.Select(t => t.Entry.Id)])
                : null;

            definition.Dest = new MacroDest { Kind = updated.DestKind, FolderIds = folderIds };
        }
        else
        {
            definition.Dest = null;
        }

        card.NotifyChanged();
        NotifyMacros();
        Save();
        Inform($"매크로를 고쳤다: {definition.Name}");
    }

    [RelayCommand]
    public void RemoveMacro(MacroCardViewModel? card)
    {
        if (card is null || !Macros.Contains(card))
        {
            return;
        }

        Workspace.Macros?.Remove(card.Definition);
        Macros.Remove(card);


        if (Workspace.Macros is { Count: 0 })
        {
            Workspace.Macros = null;
        }

        NotifyMacros();
        Save();
        Inform($"매크로를 지웠다: {card.Name}");
    }


    [RelayCommand]
    public async Task RunMacroAsync(MacroCardViewModel? card)
    {
        if (card is null || IsOperationRunning)
        {
            return;
        }

        var macro = card.Definition;
        var verb = macro.Op switch
        {
            FileOperationKind.Move => "이동",
            FileOperationKind.Trash => "휴지통",
            _ => "복사",
        };

        if (ResolveMacroSources(macro) is not { Count: > 0 } items)
        {
            Warn($"'{card.Name}' — 대상이 없다. " + (macro.Source?.Kind == MacroSourceKind.FixedPath
                ? "고정 경로가 사라졌다."
                : "먼저 그리드에서 파일을 고른다."));
            return;
        }

        var destinations = ResolveMacroDestinations(macro);

        if (macro.Op != FileOperationKind.Trash && destinations.Count == 0)
        {
            Warn($"'{card.Name}' — 목적지가 없다. 대상함 체크나 화면에 뜬 폴더를 확인한다.");
            return;
        }


        if (macro.Op == FileOperationKind.Move && destinations.Count > 1)
        {
            Warn($"'{card.Name}' — 이동인데 목적지가 {destinations.Count}개다. " +
                 "여러 곳에 두려면 복사로 바꾼다.");
            return;
        }



        IsOperationRunning = true;

        FileOperationPreview preview;
        try
        {

            preview = await _engine
                .PreviewAsync(new FileOperationRequest(
                    macro.Op, items, destinations, ConflictPolicy.Overwrite))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsOperationRunning = false;
            Warn($"'{card.Name}' — 미리보기 중 오류가 났다: {ex.Message}");
            return;
        }

        if (preview.TotalItemCount == 0)
        {
            IsOperationRunning = false;
            Warn($"'{card.Name}' — {verb}할 곳이 없다. 목적지에 닿지 못했다.");
            return;
        }


        if ((macro.Confirm || macro.Op != FileOperationKind.Copy)
            && !_prompt.Confirm($"매크로 실행 — {card.Name}", MacroPreviewText(macro, preview, items)))
        {
            IsOperationRunning = false;
            Inform($"'{card.Name}' 실행을 취소했다.");
            return;
        }



        await RunGuardedAsync(
            new FileOperationRequest(macro.Op, items, destinations, macro.OnConflict), verb)
            .ConfigureAwait(true);
    }


    private List<OperationItem> ResolveMacroSources(MacroDefinition macro)
    {
        if (macro.Source?.Kind == MacroSourceKind.FixedPath)
        {
            var path = macro.Source.Path;

            if (string.IsNullOrWhiteSpace(path))
            {
                return [];
            }

            var isDirectory = System.IO.Directory.Exists(path);



            return isDirectory || System.IO.File.Exists(path)
                ? [new OperationItem(path, isDirectory)]
                : [];
        }

        return [.. Selection.Select(i => new OperationItem(i.FullPath, i.IsDirectory))];
    }


    private List<string> ResolveMacroDestinations(MacroDefinition macro)
    {
        if (macro.Op == FileOperationKind.Trash || macro.Dest is null)
        {
            return [];
        }

        var paths = macro.Dest.Kind switch
        {

            MacroDestKind.AllVisible => Panels
                .Where(p => p.Entry is not null && p.CurrentPath is not null)
                .Select(p => p.CurrentPath!),

            MacroDestKind.FolderIds => (macro.Dest.FolderIds ?? [])
                .Select(id => Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry.Path)
                .Where(p => p is not null)
                .Select(p => p!),

            _ => CheckedDestinations.Select(t => t.Entry.Path),
        };


        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
    }


    private static string MacroPreviewText(
        MacroDefinition macro, FileOperationPreview preview, List<OperationItem> items)
    {
        var lines = new List<string>
        {
            macro.Op == FileOperationKind.Trash
                ? $"{items.Count}개를 휴지통으로 보냅니다 — 되돌릴 수 있습니다."
                : $"{items.Count}개 → 폴더 {preview.PerDestination.Count}개 = {preview.TotalItemCount}건",
        };

        if (macro.Op == FileOperationKind.Move)
        {
            lines.Add("이동이므로 원본은 사라집니다.");
        }

        if (preview.TotalOverwriteCount > 0)
        {
            var verb = macro.OnConflict switch
            {
                ConflictPolicy.Skip => "건너뜁니다",
                ConflictPolicy.Rename => "둘 다 둡니다",
                _ => "덮어씁니다",
            };

            lines.Add($"같은 이름 {preview.TotalOverwriteCount}건은 {verb}.");
        }

        if (preview.PerDestination.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(preview.PerDestination.Select(
                d => $"  → {d.Destination} ({d.ItemCount}건" +
                     (d.OverwriteCount > 0 ? $", 같은 이름 {d.OverwriteCount}" : string.Empty) + ")"));
        }

        if (preview.SkippedDestinations.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(preview.SkippedDestinations.Select(d => $"  ✕ {d} (닿을 수 없음 — 제외)"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string SuggestMacroName(int sourceCount, List<TrayEntryViewModel> checkedTray) =>
        checkedTray.Count switch
        {
            0 => $"{sourceCount}개 복사",
            1 => $"{checkedTray[0].DisplayName}(으)로 복사",
            _ => $"{checkedTray.Count}곳으로 복사",
        };

    private void NotifyMacros()
    {
        OnPropertyChanged(nameof(MacrosAreEmpty));
        OnPropertyChanged(nameof(CanCreateMacro));
        CreateMacroCommand.NotifyCanExecuteChanged();
    }







    public ConflictPolicy ConflictPolicy => Workspace.OnConflict ?? ConflictPolicy.Overwrite;


    public bool AskOnConflict => Workspace.AskOnConflict ?? true;


    public string ConflictPolicyText => AskOnConflict
        ? "같은 이름이 있으면 물어본다."
        : $"같은 이름이 있으면 {PolicyVerb(ConflictPolicy)} (묻지 않음).";


    public bool CanRestoreConflictPrompt => !AskOnConflict;

    private static string PolicyVerb(ConflictPolicy policy) => policy switch
    {
        ConflictPolicy.Skip => "건너뛴다",
        ConflictPolicy.Rename => "둘 다 둔다",
        _ => "덮어쓴다",
    };





    [RelayCommand]
    public void RestoreConflictPrompt()
    {
        Workspace.AskOnConflict = null;
        NotifyConflictPolicy();
        Save();
        Inform("같은 이름이 있으면 다시 물어본다.");
    }

    private void RememberConflictPolicy(ConflictPolicy policy)
    {

        Workspace.OnConflict = policy == ConflictPolicy.Overwrite ? null : policy;
        Workspace.AskOnConflict = false;
        NotifyConflictPolicy();
        Save();
    }

    private void NotifyConflictPolicy()
    {
        OnPropertyChanged(nameof(ConflictPolicy));
        OnPropertyChanged(nameof(AskOnConflict));
        OnPropertyChanged(nameof(ConflictPolicyText));
        OnPropertyChanged(nameof(CanRestoreConflictPrompt));
    }


    private static string ConflictMessage(
        FileOperationKind op, FileOperationPreview preview, int sourceCount)
    {
        var verb = op == FileOperationKind.Copy ? "복사" : "이동";
        var text = $"{sourceCount}개를 폴더 {preview.PerDestination.Count}개로 {verb}합니다 " +
                   $"({preview.TotalItemCount}건).{Environment.NewLine}" +
                   $"그중 {preview.TotalOverwriteCount}건이 목적지에 같은 이름으로 이미 있습니다.";

        if (op == FileOperationKind.Move)
        {
            text += $"{Environment.NewLine}이동이므로 원본은 사라집니다.";
        }

        if (preview.SkippedDestinations.Count > 0)
        {
            text += $"{Environment.NewLine}닿지 못한 목적지 " +
                    $"{preview.SkippedDestinations.Count}개는 제외됩니다.";
        }

        return text;
    }







    private const int ProgressRevealDelayMs = 400;


    private CancellationTokenSource? _operationCts;









    private bool _progressRevealPending;


    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private string? progressText;


    [ObservableProperty]
    private double progressValue;






    [RelayCommand]
    public void CancelOperation()
    {
        if (_operationCts is not { IsCancellationRequested: false } cts)
        {
            return;
        }

        cts.Cancel();
        ProgressText = "취소하는 중… (지금 처리 중인 파일까지만)";
    }

    private async Task RevealProgressSoonAsync()
    {
        await Task.Delay(ProgressRevealDelayMs).ConfigureAwait(true);


        if (_progressRevealPending)
        {
            IsProgressVisible = true;
        }
    }

    private void ApplyProgress(string verb, FileOperationProgress progress)
    {
        ProgressValue = progress.TotalItems > 0
            ? 100.0 * progress.CompletedItems / progress.TotalItems
            : 0;

        var text = $"{verb} {progress.CompletedItems}/{progress.TotalItems}건";


        if (progress.CompletedEntries > 0)
        {
            text += $" · 파일 {progress.CompletedEntries}개";
        }

        if (progress.CurrentPath is { Length: > 0 } path)
        {
            text += $" · {System.IO.Path.GetFileName(path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))}";
        }

        ProgressText = text;
    }








    private sealed class ThrottledProgress(
        SynchronizationContext? context, Action<FileOperationProgress> onReport)
        : IProgress<FileOperationProgress>
    {
        private const int MinIntervalMs = 100;

        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long _lastMs = -MinIntervalMs;
        private int _lastUnit = -1;

        public void Report(FileOperationProgress value)
        {

            var now = _clock.ElapsedMilliseconds;
            if (value.CompletedItems == _lastUnit && now - _lastMs < MinIntervalMs)
            {
                return;
            }

            _lastUnit = value.CompletedItems;
            _lastMs = now;

            if (context is null)
            {
                onReport(value);
                return;
            }

            context.Post(_ => onReport(value), null);
        }
    }







    public bool CanDropOnto(FolderEntry? destination, FileDropPayload? payload) =>
        destination is not null && payload is not null
        && CanDropOnto(destination.Path, payload.Items);















    public bool CanDropOnto(string? destinationPath, IReadOnlyList<OperationItem>? items) =>
        destinationPath is not null && items is not null
        && !IsOperationRunning && !IsEditingLayout
        && AcceptedForDrop(destinationPath, items).Count > 0;



















    public static IReadOnlyList<OperationItem> AcceptedForDrop(
        string destinationPath, IReadOnlyList<OperationItem> items)
    {

        if (WouldSwallowDestination(destinationPath, items))
        {
            return [];
        }


        return [.. items.Where(i => !CameFrom(destinationPath, i))];
    }










    private static bool WouldSwallowDestination(
        string destinationPath, IReadOnlyList<OperationItem> items) =>
        items.Any(i => i.IsDirectory
                       && (SamePath(destinationPath, i.Path) || IsUnder(destinationPath, i.Path)));


    private static bool CameFrom(string destinationPath, OperationItem item) =>
        SamePath(destinationPath, ParentFolder(item.Path));























    private static bool IsSameFolderPaste(
        string destinationPath, IReadOnlyList<OperationItem> items) =>
        items.Count > 0 && items.All(i => CameFrom(destinationPath, i));





    private static string ParentFolder(string path) =>
        System.IO.Path.GetDirectoryName(path) is { Length: > 0 } parent ? parent : path;














    public Task DropOntoFolderAsync(FolderEntry destination, FileDropPayload payload, bool copy) =>
        DropOntoPathAsync(destination.Path, payload.Items, copy);






    public async Task DropOntoPathAsync(
        string destinationPath, IReadOnlyList<OperationItem> items, bool copy)
    {
        if (!CanDropOnto(destinationPath, items))
        {
            return;
        }


        var accepted = AcceptedForDrop(destinationPath, items);

        await StartOperationAsync(
            copy ? FileOperationKind.Copy : FileOperationKind.Move,
            accepted,
            [destinationPath],
            confirmMove: false,
            sameFolderSkipped: items.Count - accepted.Count).ConfigureAwait(true);
    }








    private const uint DropEffectMove = 2;















    public async Task PasteIntoAsync(string? destinationPath)
    {
        if (destinationPath is null)
        {

            Warn("붙여넣을 곳이 없다 — 폴더를 먼저 띄워라.", transient: true);
            return;
        }

        if (_clipboard.GetData() is not { } data)
        {
            Warn("클립보드에 붙여넣을 파일이 없다.", transient: true);
            return;
        }



        var move = WantsMove(data);


        var items = Views.DropRouter.ShellItems(data);

        if (items.Count == 0)
        {
            Warn("클립보드에 붙여넣을 파일이 없다.", transient: true);
            return;
        }















        if (!move
            && !IsOperationRunning && !IsEditingLayout
            && !WouldSwallowDestination(destinationPath, items)
            && IsSameFolderPaste(destinationPath, items))
        {


            if (!_prompt.Confirm("같은 폴더에 붙여넣기", InPlaceDuplicatePreview(items)))
            {
                Inform("붙여넣기를 취소했다.");
                return;
            }

            await RunGuardedAsync(
                new FileOperationRequest(
                    FileOperationKind.Copy, items, [destinationPath], ConflictPolicy.Rename),
                "붙여넣기").ConfigureAwait(true);
            return;
        }

        if (!CanDropOnto(destinationPath, items))
        {


            Warn("여기에는 붙여넣을 것이 없다 — 온 곳과 같은 폴더이거나, 폴더를 자기 안에 " +
                 "넣는 것이거나, 다른 조작이 도는 중이다.", transient: true);
            return;
        }

        var accepted = AcceptedForDrop(destinationPath, items);

        await StartOperationAsync(
            move ? FileOperationKind.Move : FileOperationKind.Copy,
            accepted,
            [destinationPath],
            confirmMove: false,
            sameFolderSkipped: items.Count - accepted.Count,
            verbOverride: "붙여넣기").ConfigureAwait(true);
    }


    private const int MaxDuplicatePreviewLines = 5;
























    private static string InPlaceDuplicatePreview(IReadOnlyList<OperationItem> items)
    {
        var lines = new List<string>
        {
            items.Count == 1
                ? "온 곳과 같은 폴더입니다 — 사본을 하나 만듭니다."
                : $"온 곳과 같은 폴더입니다 — 사본 {items.Count}개를 만듭니다.",
            string.Empty,
        };

        lines.AddRange(items.Take(MaxDuplicatePreviewLines).Select(
            i => $"  {System.IO.Path.GetFileName(i.Path)} → " +
                 $"{System.IO.Path.GetFileName(FileOperationEngine.PreviewUniqueName(i.Path))}"));

        if (items.Count > MaxDuplicatePreviewLines)
        {
            lines.Add($"  … 외 {items.Count - MaxDuplicatePreviewLines}개");
        }

        lines.Add(string.Empty);
        lines.Add("원본은 그대로 남습니다.");

        return string.Join(Environment.NewLine, lines);
    }













    private static bool WantsMove(System.Windows.IDataObject data)
    {
        if (data.GetData(Views.ShellExport.PreferredDropEffectFormat)
            is not System.IO.MemoryStream stream)
        {
            return false;
        }

        var bytes = stream.ToArray();

        return bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == DropEffectMove;
    }










    private void ReportExported(int itemCount) =>
        Inform($"{itemCount}개를 앱 밖으로 넘겼다 — 그 뒤는 셸이 한다.");



    public bool CanTrashSelection => HasSelection && !IsOperationRunning;










    [RelayCommand(CanExecute = nameof(CanTrashSelection))]
    public async Task TrashSelectionAsync()
    {
        var sources = Selection;

        if (sources.Count == 0 || IsOperationRunning)
        {
            return;
        }

        var request = new FileOperationRequest(
            FileOperationKind.Trash,
            [.. sources.Select(i => new OperationItem(i.FullPath, i.IsDirectory))],
            [],
            ConflictPolicy.Overwrite);

        await RunGuardedAsync(
            request,
            "휴지통",
            successMessage: $"{sources.Count}개를 휴지통으로 보냈다 — 되돌릴 수 있다.")
            .ConfigureAwait(true);
    }


    [RelayCommand]
    public void RejectPermanentDelete() =>
        Warn("영구 삭제는 하지 않는다. Del 로 휴지통에 보내면 되돌릴 수 있다.", transient: true);



    private static bool SamePath(string a, string b) =>
        string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string parent)
    {
        var full = FullPath(path);
        var root = FullPath(parent);

        return full.Length > root.Length
               && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && (full[root.Length] == System.IO.Path.DirectorySeparatorChar
                   || full[root.Length] == System.IO.Path.AltDirectorySeparatorChar);
    }


    private static string FullPath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path).TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.PathTooLongException
                                       or NotSupportedException)
        {
            return path;
        }
    }

    private static string MoveWarning(
        FileOperationPreview preview, int sourceCount, ConflictPolicy policy)
    {
        var text = $"파일 {sourceCount}개를 폴더 {preview.PerDestination.Count}개로 옮긴다 " +
                   $"({preview.TotalItemCount}건).{Environment.NewLine}원본은 사라진다.";


        if (preview.TotalOverwriteCount > 0)
        {
            text += $"{Environment.NewLine}같은 이름 {preview.TotalOverwriteCount}건은 " +
                    $"{PolicyVerb(policy)}.";
        }

        if (preview.SkippedDestinations.Count > 0)
        {
            text += $"{Environment.NewLine}닿지 못한 목적지 " +
                    $"{preview.SkippedDestinations.Count}개는 제외된다.";
        }

        return text;
    }












    private void ReportOperation(
        string verb,
        FileOperationReport report,
        string? successMessage = null,
        int sameFolderSkipped = 0)
    {
        var ok = report.Results.Count(r => r.Status == OperationItemStatus.Succeeded);
        var skipped = report.Results.Count(r => r.Status == OperationItemStatus.Skipped);
        var failed = report.Results.Count(r => r.Status == OperationItemStatus.Failed);
        var overwritten = report.Results.Count(r => r.Overwritten);

        var summary = $"{verb} — 성공 {ok} · 건너뜀 {skipped} · 실패 {failed}";
        if (sameFolderSkipped > 0)
        {
            summary += $" · 온 곳과 같은 폴더 {sameFolderSkipped}";
        }

        if (overwritten > 0)
        {

            summary += $" · 덮어씀 {overwritten}";
        }

        if (report.Canceled)
        {
            summary += " · 취소됨";
        }

        if (failed == 0 && skipped == 0 && sameFolderSkipped == 0)
        {
            Inform(report.Canceled || successMessage is null ? summary : successMessage);
            return;
        }


        Warn(summary);




        if (failed == 0 && skipped == 0)
        {
            return;
        }

        var lines = report.Results
            .Where(r => r.Status != OperationItemStatus.Succeeded)
            .Select(r => $"[{(r.Status == OperationItemStatus.Skipped ? "건너뜀" : "실패")}] " +
                         $"{System.IO.Path.GetFileName(r.SourcePath)} -> " +
                         $"{(string.IsNullOrEmpty(r.Destination) ? "휴지통" : r.Destination)} : {r.Reason}");

        _prompt.Report(
            $"{verb} 결과",
            summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    private Task RefreshAllPanelsAsync() =>
        Task.WhenAll(Panels.Where(p => p.Entry is not null).Select(p => p.RefreshAsync()));




    public async Task InitializeAsync()
    {
        Workspace.LastUsed = DateTimeOffset.Now;
        _store.SaveWorkspaceDebounced(Workspace);

        var loads = Panels
            .Where(p => p.Entry is not null)
            .Select(p => p.GoToAnchorAsync());



        var probes = Rows.Select(ProbeRowAsync).ToList();

        await Task.WhenAll(loads.Concat(probes)).ConfigureAwait(true);
        UpdateRowStates();
    }









    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ShowFolderAsync(FolderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }


        if (IsEditingLayout)
        {
            return;
        }





        var targetTile = PinnedTileFor(row) ?? ResolveRotatingTile(row.RotationSlot);
        if (targetTile is not null && !ReferenceEquals(targetTile, ExpandedTile))
        {
            ExpandedTile = null;
        }

        if (PinnedTileFor(row) is { } pinned)
        {


            var wasAway = pinned.Panel.IsAwayFromAnchor;
            await pinned.Panel.GoToAnchorAsync().ConfigureAwait(true);

            if (wasAway)
            {
                ClearMessage();
            }
            else
            {
                Inform($"상시 칸에 이미 떠 있다: {row.DisplayName}");
            }
            UpdateRowStates();
            return;
        }

        var wanted = row.RotationSlot;
        var tile = ResolveRotatingTile(wanted);

        if (tile is null)
        {
            Warn("순환 칸이 없다. 배치 편집에서 빈 칸을 순환 칸으로 만들어라.");
            return;
        }



        if (tile.RotationIndex == wanted)
        {
            ClearMessage();
        }
        else
        {
            Inform($"순환{wanted} 칸 없음 → {tile.KindTag}");
        }

        ActiveRotatingTile = tile;


        if (ReferenceEquals(tile.Panel.Entry, row.Entry))
        {
            await tile.Panel.GoToAnchorAsync().ConfigureAwait(true);
        }
        else
        {
            await tile.Panel.ShowAsync(row.Entry).ConfigureAwait(true);
        }

        await ProbeRowAsync(row).ConfigureAwait(true);
        UpdateRowStates();
    }


    public TileViewModel? PinnedTileFor(FolderRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return Tiles.FirstOrDefault(t => t.IsPinned && t.Spec.FolderId == row.Entry.Id);
    }




    public TileViewModel? ResolveRotatingTile(int slot)
    {
        var rotating = Tiles.Where(t => t.IsRotating).ToList();

        return rotating.FirstOrDefault(t => t.RotationIndex == slot)
               ?? rotating.FirstOrDefault(t => t.RotationIndex == 1)
               ?? rotating.OrderBy(t => t.RotationIndex ?? int.MaxValue).FirstOrDefault();
    }


    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAllAsync()
    {


        ExpireTransientMessage();

        await Task.WhenAll(Panels.Where(p => p.Entry is not null).Select(p => p.RefreshAsync()))
            .ConfigureAwait(true);
        await Task.WhenAll(Rows.Select(ProbeRowAsync)).ConfigureAwait(true);

        UpdateRowStates();
    }


    public void Shutdown()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

        _everythingSearchService?.Dispose();

        _store.SaveFailed -= OnSaveFailed;
        _store.Flush();
    }




    public bool FoldersCollapsed
    {
        get => Workspace.LeftLayout?.FoldersCollapsed ?? false;
        set
        {
            if (FoldersCollapsed == value)
            {
                return;
            }

            LeftLayout().FoldersCollapsed = value;
            OnPropertyChanged();
            SaveLeftLayout();
        }
    }


    public bool LowerCollapsed
    {
        get => Workspace.LeftLayout?.LowerCollapsed ?? false;
        set
        {
            if (LowerCollapsed == value)
            {
                return;
            }

            LeftLayout().LowerCollapsed = value;
            OnPropertyChanged();
            SaveLeftLayout();
        }
    }


    public bool ShowLeftSplitter => !FoldersCollapsed && !LowerCollapsed;













    public bool LeftPanelCollapsed
    {
        get => Workspace.LeftLayout?.LeftPanelCollapsed ?? false;
        set
        {
            if (LeftPanelCollapsed == value)
            {
                return;
            }

            LeftLayout().LeftPanelCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LeftPanelToggleText));
            OnPropertyChanged(nameof(LeftPanelToggleTip));
            SaveLeftLayout();
        }
    }


    public string LeftPanelToggleText => LeftPanelCollapsed ? "▶" : "◀";

    public string LeftPanelToggleTip => LeftPanelCollapsed
        ? "왼쪽 패널(폴더 · 대상함 · 매크로) 펴기"
        : "왼쪽 패널(폴더 · 대상함 · 매크로) 접기 — 그리드가 창을 다 쓴다";

    [RelayCommand]
    public void ToggleLeftPanel() => LeftPanelCollapsed = !LeftPanelCollapsed;

    [RelayCommand]
    public void ToggleFolders() => FoldersCollapsed = !FoldersCollapsed;

    [RelayCommand]
    public void ToggleLower() => LowerCollapsed = !LowerCollapsed;


    private LeftLayoutSpec LeftLayout() => Workspace.LeftLayout ??= new LeftLayoutSpec();

    private void SaveLeftLayout()
    {
        OnPropertyChanged(nameof(ShowLeftSplitter));
        Save();
    }




    public void AddFolders(IEnumerable<string> selected)
    {
        var paths = selected.ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var added = 0;
        var duplicates = 0;

        foreach (var path in paths)
        {
            if (TryRegisterFolderPath(path))
            {
                added++;
            }
            else
            {
                duplicates++;
            }
        }

        if (added == 0)
        {
            Warn("고른 폴더가 모두 이미 등록되어 있다.", transient: true);
            return;
        }

        RefreshRotationChoices();
        Save();

        var duplicateText = duplicates > 0 ? $" · 이미 등록 {duplicates}개 제외" : string.Empty;
        Inform($"폴더 {added}개를 등록했다{duplicateText}.");
    }












    [RelayCommand]
    public void RegisterFolder(FileItemViewModel? item)
    {


        if (item is null || !item.IsDirectory)
        {
            return;
        }

        RegisterFolderAtPath(item.FullPath);
    }







    private void RegisterFolderAtPath(string path)
    {
        if (!TryRegisterFolderPath(path))
        {
            Warn($"이미 등록된 폴더다: {FolderPathRules.Normalize(path)}", transient: true);
            return;
        }

        RefreshRotationChoices();
        Save();
        Inform($"등록했다: {FolderPathRules.LeafName(path)}");
    }










    private bool TryRegisterFolderPath(string path)
    {
        var normalized = FolderPathRules.Normalize(path);




        if (Workspace.Folders.Any(f => PathsEqual(f.Path, normalized)))
        {
            return false;
        }

        var entry = new FolderEntry
        {
            Path = normalized,


            DisplayName = FolderPathRules.LeafName(normalized),
        };

        Workspace.Folders.Add(entry);
        Rows.Add(new FolderRowViewModel(entry));
        return true;
    }
















    [RelayCommand]
    public void RemoveFolder(FolderRowViewModel? row)
    {
        if (row is null || !Rows.Contains(row))
        {
            return;
        }

        var id = row.Entry.Id;





        var doomedTiles = Tiles
            .Where(t => t.Spec.Kind == TileKind.Pinned && t.Spec.FolderId == id)
            .ToList();


        var trayCount = Workspace.CopyTray?.Count(g => g == id) ?? 0;


        var doomedMacros = (Workspace.Macros ?? [])
            .Where(m => m.Dest?.FolderIds?.Contains(id) == true)
            .ToList();

        var confirmed = _prompt.Confirm(
            "폴더 제거",
            $"'{row.DisplayName}' 등록을 지운다.\n\n" +
            $"타일 {doomedTiles.Count}개 · 대상함 {trayCount}개 · 매크로 {doomedMacros.Count}개가 같이 정리된다.\n\n" +
            "실제 폴더는 지워지지 않는다 — 작업 관리의 등록만 없어진다.");

        if (!confirmed)
        {
            return;
        }



        foreach (var tile in doomedTiles)
        {




            if (ReferenceEquals(ExpandedTile, tile))
            {
                ExpandedTile = null;
            }

            Workspace.Tiles!.Remove(tile.Spec);
            Tiles.Remove(tile);
            tile.Panel.Clear();
            DetachWatcher(tile);
        }


        foreach (var tile in Tiles.Where(t => t.IsRotating && ReferenceEquals(t.Panel.Entry, row.Entry)))
        {
            tile.Panel.Clear();
        }

        foreach (var entry in Tray.Where(t => t.Entry.Id == id).ToList())
        {
            Tray.Remove(entry);
        }

        foreach (var macro in doomedMacros)
        {
            macro.Dest!.FolderIds!.RemoveAll(g => g == id);


            if (macro.Dest.FolderIds.Count == 0)
            {
                macro.Dest.FolderIds = null;
            }

            Macros.FirstOrDefault(c => ReferenceEquals(c.Definition, macro))?.NotifyChanged();
        }

        Rows.Remove(row);
        Workspace.Folders.Remove(row.Entry);





        if (trayCount > 0)
        {
            SaveTray();
        }

        RefreshDerivedLayout();
        UpdateRowStates();
        Save();

        Inform($"등록을 지웠다: {row.DisplayName} " +
               $"(타일 {doomedTiles.Count} · 대상함 {trayCount} · 매크로 {doomedMacros.Count})");
    }












    [RelayCommand]
    public void EditFolder(FolderRowViewModel? row)
    {
        if (row is null || !Rows.Contains(row))
        {
            return;
        }

        var edited = _folderEditor.Edit(
            new FolderEditDraft(row.Entry.DisplayName, row.Entry.Description), row.Entry.Path);

        if (edited is null)
        {
            return;
        }

        row.Entry.DisplayName = edited.DisplayName;
        row.Entry.Description = edited.Description;



        row.NotifyEdited();

        foreach (var entry in Tray.Where(t => ReferenceEquals(t.Entry, row.Entry)))
        {
            entry.NotifyEdited();
        }

        foreach (var panel in Panels.Where(p => ReferenceEquals(p.Entry, row.Entry)))
        {
            panel.NotifyAnchorName();
        }

        Save();
        Inform($"폴더를 고쳤다: {row.DisplayName}");
    }






















    private bool SetAnchorFromItem(FolderPanelViewModel panel, FileItemViewModel item) =>
        item.IsDirectory && SetAnchorFromPath(panel, item.FullPath);








    private bool SetAnchorFromPath(FolderPanelViewModel panel, string path)
    {
        if (panel.Entry is not { } entry)
        {
            return false;
        }


        var row = Rows.FirstOrDefault(r => ReferenceEquals(r.Entry, entry));
        if (row is null)
        {
            return false;
        }

        var normalized = FolderPathRules.Normalize(path);


        if (PathsEqual(entry.Path, normalized))
        {
            Warn($"이미 앵커다: {normalized}", transient: true);
            return false;
        }


        if (Workspace.Folders.Any(f => !ReferenceEquals(f, entry) && PathsEqual(f.Path, normalized)))
        {
            Warn($"이미 등록된 폴더다: {normalized}", transient: true);
            return false;
        }



        var leaf = FolderPathRules.LeafName(normalized);
        if (_prompt.Confirm(
                "앵커 지정",
                $"'{row.DisplayName}' 의 앵커를 다음으로 옮긴다:\n{normalized}\n\n" +
                $"표시 이름도 '{leaf}' 로 바꿀까?"))
        {
            entry.DisplayName = leaf;
        }

        entry.Path = normalized;



        row.NotifyEdited();

        foreach (var trayEntry in Tray.Where(t => ReferenceEquals(t.Entry, entry)))
        {
            trayEntry.NotifyEdited();
        }

        foreach (var p in Panels.Where(p => ReferenceEquals(p.Entry, entry)))
        {
            p.NotifyAnchorName();
        }


        Save();

        Inform($"앵커를 옮겼다: {row.DisplayName} → {normalized}");
        return true;
    }























    private string? PromptNewFolderName(string suggested) =>
        _prompt.AskName("새 폴더 만들기", "새 폴더의 이름을 적어라.", suggested, "만들기");









    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComparing))]
    private TileViewModel? comparePeerA;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComparing))]
    private TileViewModel? comparePeerB;

    public bool IsComparing => ComparePeerA is not null && ComparePeerB is not null;

    private EventHandler? _compareInvalidationHandler;











    public bool IsShowingComparePicker { get; private set; }


    private void OnCompareRequested(FolderPanelViewModel panel)
    {
        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Panel, panel));
        if (tile is null)
        {
            return;
        }

        if (tile.IsSearch)
        {
            Warn("검색 타일은 비교 대상이 아니다 — 폴더 하나가 아니다.", transient: true);
            return;
        }


        if (IsComparing && (ReferenceEquals(tile, ComparePeerA) || ReferenceEquals(tile, ComparePeerB)))
        {
            ClearCompare();
            return;
        }

        var candidates = Tiles
            .Where(t => !ReferenceEquals(t, tile) && !t.IsSearch && t.Panel.CurrentPath is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            Warn("비교할 다른 타일이 없다 — 폴더가 열린 타일이 이것 하나뿐이다.", transient: true);
            return;
        }

        int? picked;
        IsShowingComparePicker = true;
        try
        {
            picked = _prompt.PickOne(
                "타일 비교", "어느 타일과 비교할까?", candidates.Select(t => t.EditLabel).ToList());
        }
        finally
        {
            IsShowingComparePicker = false;
        }

        if (picked is { } index)
        {
            RunCompare(tile, candidates[index]);
        }
        else
        {




            Inform("비교를 취소했다");
        }
    }

    private void RunCompare(TileViewModel a, TileViewModel b)
    {
        ClearCompare();

        var (leftEntries, rightEntries) = TileComparer.Compare(
            [.. a.Panel.Items.Select(i => i.Item)],
            [.. b.Panel.Items.Select(i => i.Item)]);
        ApplyCompareBadges(a.Panel.Items, leftEntries);
        ApplyCompareBadges(b.Panel.Items, rightEntries);

        ComparePeerA = a;
        ComparePeerB = b;

        _compareInvalidationHandler = (_, _) => ClearCompare();
        a.Panel.LoadCompleted += _compareInvalidationHandler;
        b.Panel.LoadCompleted += _compareInvalidationHandler;

        Inform($"비교 시작 — {a.EditLabel} ↔ {b.EditLabel}");
    }

    private static void ApplyCompareBadges(
        IReadOnlyList<FileItemViewModel> items, IReadOnlyList<CompareEntry> entries)
    {
        var statusByPath = entries.ToDictionary(
            entry => entry.Item.FullPath, entry => entry.Status, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            item.CompareStatus = statusByPath.GetValueOrDefault(item.FullPath, CompareStatus.Unset);
        }
    }


    [RelayCommand]
    private void ClearCompare()
    {
        if (ComparePeerA is not null)
        {
            foreach (var item in ComparePeerA.Panel.Items)
            {
                item.CompareStatus = CompareStatus.Unset;
            }

            if (_compareInvalidationHandler is not null)
            {
                ComparePeerA.Panel.LoadCompleted -= _compareInvalidationHandler;
            }
        }

        if (ComparePeerB is not null)
        {
            foreach (var item in ComparePeerB.Panel.Items)
            {
                item.CompareStatus = CompareStatus.Unset;
            }

            if (_compareInvalidationHandler is not null)
            {
                ComparePeerB.Panel.LoadCompleted -= _compareInvalidationHandler;
            }
        }

        _compareInvalidationHandler = null;
        ComparePeerA = null;
        ComparePeerB = null;
    }








    public FileItemViewModel? FindCompareCounterpart(FolderPanelViewModel panel, FileItemViewModel item)
    {
        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Panel, panel));
        if (tile is null)
        {
            return null;
        }

        var peer = ReferenceEquals(tile, ComparePeerA) ? ComparePeerB
            : ReferenceEquals(tile, ComparePeerB) ? ComparePeerA
            : null;

        return peer?.Panel.Items.FirstOrDefault(i =>
            !i.IsDirectory && string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase));
    }


    private void OnCompareContentRequested(FolderPanelViewModel panel, FileItemViewModel item)
    {
        var counterpart = FindCompareCounterpart(panel, item);
        if (counterpart is null)
        {
            Inform("상대 타일에서 같은 이름의 파일을 더는 못 찾았다 — 비교가 갱신됐을 수 있다");
            return;
        }

        var (leftText, leftError) = FileContentReader.TryReadText(item.FullPath);
        if (leftError is not null)
        {
            Inform(leftError);
            return;
        }

        var (rightText, rightError) = FileContentReader.TryReadText(counterpart.FullPath);
        if (rightError is not null)
        {
            Inform(rightError);
            return;
        }

        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Panel, panel));
        var peerTile = ReferenceEquals(tile, ComparePeerA) ? ComparePeerB : ComparePeerA;

        var diff = FileContentDiffer.Diff(leftText!, rightText!);
        var window = new ContentDiffWindow(
            item.Name,
            tile?.EditLabel ?? "?", item.FullPath,
            peerTile?.EditLabel ?? "?", counterpart.FullPath,
            diff,
            path => _shell.Open(path))
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };
        window.Show();
    }








    public IReadOnlyList<DuplicateGroup> FindDuplicateGroups()
    {
        var entries = Panels.SelectMany(panel =>
        {
            var label = Tiles.FirstOrDefault(t => ReferenceEquals(t.Panel, panel))?.EditLabel ?? "?";
            return panel.Items.Select(item => (TileLabel: label, item.Item));
        });

        return DuplicateFinder.FindGroups(entries);
    }






    [RelayCommand]
    private void FindDuplicates()
    {
        var groups = FindDuplicateGroups();
        var window = new DuplicateFinderWindow(groups, path => _shell.RevealInExplorer(path, isDirectory: false))
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };
        window.Show();
    }

    private string? RenameFromItem(FileItemViewModel item)
    {
        var oldPath = FolderPathRules.Normalize(item.FullPath);
        var oldLeaf = FolderPathRules.LeafName(oldPath);


        if (System.IO.Path.GetDirectoryName(oldPath) is not { Length: > 0 } parent)
        {
            Warn($"이름을 바꿀 수 없다 — 드라이브 루트다: {oldPath}", transient: true);
            return null;
        }

        var typed = _prompt.AskName(
            "이름 바꾸기",
            $"'{oldLeaf}' 의 새 이름을 적어라. {(item.IsDirectory ? "폴더" : "파일")}가 실제로 바뀐다.",
            oldLeaf,
            "바꾸기");


        if (typed is null)
        {
            return null;
        }

        return TryRenameLeafOnDisk(item, oldPath, parent, oldLeaf, typed).NewPath;
    }












    private (string? NewPath, string? FailureReason) TryRenameLeafOnDisk(
        FileItemViewModel item, string oldPath, string parent, string oldLeaf, string typed)
    {


        var leaf = typed.Trim();

        if (FolderPathRules.RejectLeafName(leaf) is { } reason)
        {
            Warn($"이름을 바꾸지 못했다 — {reason}", transient: true);
            return (null, reason);
        }



        if (string.Equals(leaf, oldLeaf, StringComparison.Ordinal))
        {
            return (null, null);
        }

        var newPath = System.IO.Path.Combine(parent, leaf);

        if (!PathsEqual(oldPath, newPath)
            && (System.IO.Directory.Exists(newPath) || System.IO.File.Exists(newPath)))
        {
            var conflictReason = $"같은 이름이 이미 있다: {leaf}";
            Warn($"이름을 바꾸지 못했다 — {conflictReason}", transient: true);
            return (null, conflictReason);
        }

        try
        {
            if (item.IsDirectory)
            {
                System.IO.Directory.Move(oldPath, newPath);
            }
            else
            {
                System.IO.File.Move(oldPath, newPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                     or System.IO.IOException
                                     or ArgumentException)
        {

            Warn($"이름을 바꾸지 못했다: {oldLeaf} — {ex.Message}");
            return (null, ex.Message);
        }

        UpdateAnchorAfterRename(oldPath, newPath, leaf, oldLeaf);
        return (newPath, null);
    }





    private async Task OnBatchRenameRequestedAsync(FolderPanelViewModel panel)
    {
        var selected = panel.SelectedItems;
        if (selected.Count == 0)
        {
            return;
        }

        var selectedNames = new HashSet<string>(
            selected.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        var otherNames = panel.Items
            .Select(i => i.Name)
            .Where(name => !selectedNames.Contains(name))
            .ToList();

        if (_prompt.PlanBatchRename(selected.Select(i => i.Name).ToList(), otherNames) is not { } rows)
        {
            return;
        }

        ExecuteBatchRename(selected, rows);
        await panel.RefreshAsync().ConfigureAwait(true);
    }






    private void ExecuteBatchRename(
        IReadOnlyList<FileItemViewModel> selected, IReadOnlyList<RenamePreviewRow> rows)
    {
        var succeeded = 0;
        var failures = new List<string>();

        for (var index = 0; index < selected.Count; index++)
        {
            var row = rows[index];
            if (row.IsUnchanged)
            {
                continue;
            }

            var item = selected[index];
            var oldPath = FolderPathRules.Normalize(item.FullPath);
            var oldLeaf = FolderPathRules.LeafName(oldPath);

            if (System.IO.Path.GetDirectoryName(oldPath) is not { Length: > 0 } parent)
            {
                failures.Add($"{oldLeaf} — 드라이브 루트라 이름을 바꿀 수 없다");
                continue;
            }

            var (newPath, failureReason) = TryRenameLeafOnDisk(item, oldPath, parent, oldLeaf, row.NewName);
            if (newPath is not null)
            {
                succeeded++;
            }
            else if (failureReason is not null)
            {
                failures.Add($"{oldLeaf} — {failureReason}");
            }
        }

        if (failures.Count == 0)
        {
            Inform($"일괄 이름 바꾸기 — {succeeded}개 바꿨다.");
        }
        else
        {

            _prompt.Report(
                "일괄 이름 바꾸기 결과",
                $"{succeeded}개 바꿨다, {failures.Count}개 실패:\n" + string.Join("\n", failures));
        }
    }


























    private void UpdateAnchorAfterRename(string oldPath, string newPath, string leaf, string oldLeaf)
    {

        var row = Rows.FirstOrDefault(r => PathsEqual(r.Entry.Path, oldPath));

        if (row is null)
        {

            Inform($"이름을 바꿨다: {oldLeaf} → {leaf}");
            return;
        }



        var label = row.DisplayName;

        row.Entry.Path = newPath;




        if (_prompt.Confirm(
                "이름 바꾸기",
                $"'{label}' 등록의 앵커 폴더 이름이 바뀌었다:\n{newPath}\n\n" +
                $"표시 이름도 '{leaf}' 로 바꿀까?"))
        {
            row.Entry.DisplayName = leaf;
        }



        row.NotifyEdited();

        foreach (var trayEntry in Tray.Where(t => ReferenceEquals(t.Entry, row.Entry)))
        {
            trayEntry.NotifyEdited();
        }

        foreach (var panel in Panels.Where(p => ReferenceEquals(p.Entry, row.Entry)))
        {
            panel.NotifyAnchorName();
        }


        Save();

        Inform($"이름을 바꿨다: {oldLeaf} → {leaf} (등록 '{row.DisplayName}' 의 앵커도 따라갔다)");
    }


    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            FolderPathRules.Normalize(a), FolderPathRules.Normalize(b), StringComparison.OrdinalIgnoreCase);



    private TileViewModel CreateTile(TileSpec spec)
    {
        var isSearchTile = spec.Kind == TileKind.Search;



        if (isSearchTile)
        {
            _everythingSearchService ??= new EverythingSearchService(new EverythingNative());
        }

        var panel = new FolderPanelViewModel(
            _enumerator, _shell, _clipboard, spec.Kind == TileKind.Rotating, isSearchTile,
            ReportError, ReportRejection)
        {


            ExtensionGlyphs = _settings.ExtensionGlyphs,




            ShellIconService = _settings.ShowShellIcons ? _shellIconService : null,


            TrashSelectionCommand = TrashSelectionCommand,


            ShellDropHost = this,


            ReportExported = ReportExported,



            SaveFolderEntry = Save,


            RegisterFolderCommand = RegisterFolderCommand,



            SetAnchorFolder = SetAnchorFromItem,



            SetAnchorFolderToPath = SetAnchorFromPath,
            RegisterFolderPath = RegisterFolderAtPath,


            PromptForNewFolderName = PromptNewFolderName,



            RenameOnDisk = RenameFromItem,



            CompareRequested = OnCompareRequested,




            CompareContentRequested = OnCompareContentRequested,



            BatchRenameRequested = OnBatchRenameRequestedAsync,



            PasteInto = PasteIntoAsync,




            ToggleExpandCommand = ToggleExpandCommand,
        };

        if (isSearchTile)
        {
            panel.EverythingSearchService = _everythingSearchService;
            panel.EverythingMaxResults = _settings.EverythingMaxResults;
        }

        if (spec.Kind == TileKind.Pinned && spec.FolderId is { } id)
        {
            panel.Entry = Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry;
        }

        var tile = new TileViewModel(spec, panel) { IsEditing = IsEditingLayout };
        AttachWatcher(tile);
        return tile;
    }










    private void AttachWatcher(TileViewModel tile)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }



        _watchers[tile] = new FolderWatcher(tile.Panel, dispatcher, () => IsOperationRunning);
    }

    private void DetachWatcher(TileViewModel tile)
    {
        if (_watchers.Remove(tile, out var watcher))
        {
            watcher.Dispose();
        }
    }

    private TileViewModel AddTile(TileSpec spec)
    {
        var tile = CreateTile(spec);
        Workspace.Tiles!.Add(spec);
        Tiles.Add(tile);
        RefreshDerivedLayout();
        Save();
        return tile;
    }

    private void CommitPlacement(TileViewModel tile)
    {
        tile.NotifyPlacement();
        RefreshDerivedLayout();
        Save();
    }


    private int DemoteFoldersTo(int from, int to)
    {
        var moved = 0;

        foreach (var row in Rows.Where(r => r.RotationSlot == from))
        {
            row.Entry.RotationSlot = to <= 1 ? null : to;
            row.NotifyRotationSlot();
            moved++;
        }

        return moved;
    }


    private void RefreshDerivedLayout()
    {
        foreach (var panel in Panels)
        {
            panel.PropertyChanged -= OnPanelPropertyChanged;
        }

        Panels = [.. Tiles.Select(t => t.Panel)];

        foreach (var panel in Panels)
        {
            panel.PropertyChanged += OnPanelPropertyChanged;
        }

        var free = GridLayout.FreeCells(Specs, GridCols, GridRows).ToHashSet();
        foreach (var cell in Cells)
        {
            cell.IsFree = free.Contains((cell.X, cell.Y));
        }

        if (ActiveRotatingTile is { } active && !Tiles.Contains(active))
        {
            ActiveRotatingTile = null;
        }

        RefreshRotationChoices();

        OnPropertyChanged(nameof(Panels));
        OnPropertyChanged(nameof(RotatingPanel));
        OnPropertyChanged(nameof(StatusPath));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasSearchTile));
    }




    private void RefreshRotationChoices()
    {
        var existing = Tiles
            .Where(t => t.IsRotating)
            .Select(t => t.RotationIndex ?? 1)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        var showTag = existing.Count > 1;



        var pinnedFolders = Tiles
            .Where(t => t.IsPinned && t.Spec.FolderId is not null)
            .Select(t => t.Spec.FolderId!.Value)
            .ToHashSet();

        var pinnedFlagChanged = false;

        foreach (var row in Rows)
        {
            row.HasPinnedTile = pinnedFolders.Contains(row.Entry.Id);
            row.ShowRotationTag = showTag && !row.HasPinnedTile;



            if (row.Entry.Pinned != row.HasPinnedTile)
            {
                row.Entry.Pinned = row.HasPinnedTile;
                pinnedFlagChanged = true;
            }



            if (row.HasPinnedTile)
            {
                row.RotationChoices = [];
                continue;
            }

            List<int> slots = existing.Contains(row.RotationSlot)
                ? existing
                : [.. existing, row.RotationSlot];

            row.RotationChoices =
            [
                .. slots.Order().Select(slot => new RotationChoiceViewModel(row, slot, ApplyRotationChoice)
                {
                    IsSelected = row.RotationSlot == slot,
                    Exists = existing.Contains(slot),
                }),
            ];
        }

        SyncRotatingRows();

        if (pinnedFlagChanged)
        {
            Save();
        }
    }









    private void SyncRotatingRows()
    {
        var wanted = Rows.Where(r => !r.HasPinnedTile).ToList();

        if (RotatingRows.Count == wanted.Count
            && RotatingRows.Zip(wanted).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        RotatingRows.Clear();
        foreach (var row in wanted)
        {
            RotatingRows.Add(row);
        }
    }

    private async Task ProbeRowAsync(FolderRowViewModel row)
    {
        var failure = await _enumerator.ProbeAsync(row.Entry.Path).ConfigureAwait(true);
        row.IsInaccessible = failure is not null;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderPanelViewModel.SelectedItems)
            && sender is FolderPanelViewModel panel)
        {
            OnPanelSelectionChanged(panel);
            return;
        }

        if (e.PropertyName is nameof(FolderPanelViewModel.CurrentPath)
            or nameof(FolderPanelViewModel.Entry)
            or nameof(FolderPanelViewModel.Failure)
            or nameof(FolderPanelViewModel.LocationHint))
        {
            OnPropertyChanged(nameof(StatusPath));
            UpdateRowStates();
        }
    }


    private void UpdateRowStates()
    {
        foreach (var row in Rows)
        {


            var rotating = Tiles.FirstOrDefault(
                t => t.IsRotating && ReferenceEquals(t.Panel.Entry, row.Entry));
            var panel = rotating?.Panel
                        ?? Panels.FirstOrDefault(p => ReferenceEquals(p.Entry, row.Entry));

            row.IsShowingInRotating = rotating is not null;
            row.LocationHint = panel?.LocationHint;








            if (panel is not null)
            {
                row.IsInaccessible = panel.Failure is not null;
            }
        }
    }

    private void Save() => _store.SaveWorkspaceDebounced(Workspace);

    private void OnSaveFailed(object? sender, StorageFailure failure)
    {
        var text = $"저장 실패: {failure.Message}";
        if (_uiContext is null)
        {
            ReportError(text);
            return;
        }

        _uiContext.Post(_ => ReportError(text), null);
    }

    private void ReportError(string text) => Warn(text);


    private void ReportRejection(string text) => Warn(text, transient: true);
}
