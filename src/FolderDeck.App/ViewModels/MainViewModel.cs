using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.ViewModels;

/// <summary>메인 창 (§3.2) — 좌우 2단.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IWorkspaceStore _store;
    private readonly IFolderEnumerator _enumerator;
    private readonly IShellLauncher _shell;
    private readonly IClipboardService _clipboard;
    private readonly IFileOperationEngine _engine;
    private readonly IUserPrompt _prompt;

    /// <summary>디바운스 저장 실패는 스레드 풀에서 올라온다 — UI 스레드로 되돌리기 위해 잡아 둔다.</summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public MainViewModel(
        Workspace workspace,
        IWorkspaceStore store,
        IFolderEnumerator enumerator,
        IShellLauncher shell,
        IClipboardService clipboard,
        IFileOperationEngine engine,
        IUserPrompt prompt)
    {
        Workspace = workspace;
        _store = store;
        _enumerator = enumerator;
        _shell = shell;
        _clipboard = clipboard;
        _engine = engine;
        _prompt = prompt;

        Rows = [.. workspace.Folders.Select(f => new FolderRowViewModel(f))];

        // v1/v2 → v3: tiles[] 가 없으면 기존 pinned 유도 규칙으로 한 번 만들고, 손으로 고쳐
        // 격자를 벗어난 타일은 다듬는다. 둘 중 하나라도 일어나면 저장해서 v3 로 승격시킨다.
        var grid = GridLayout.EnsureGrid(workspace);
        GridCols = grid.Cols;
        GridRows = grid.Rows;

        var derived = GridLayout.EnsureTiles(workspace);
        var dropped = GridLayout.NormalizeTiles(workspace);

        Tiles = [.. workspace.Tiles!.Select(CreateTile)];

        Cells = [.. Enumerable.Range(0, GridRows)
            .SelectMany(y => Enumerable.Range(0, GridCols).Select(x => new GridCellViewModel(x, y)))];

        // 대상함: copyTray[] 는 폴더 id 목록이다. 사라진 id 는 조용히 흘린다 —
        // 폴더 등록이 없어졌으면 목적지로 쓸 수도 없다.
        Tray = [.. (workspace.CopyTray ?? [])
            .Select(id => Rows.FirstOrDefault(r => r.Entry.Id == id))
            .Where(r => r is not null)
            .Select(r => new TrayEntryViewModel(r!.Entry, RemoveFromTray))];

        RefreshDerivedLayout();

        if (derived || dropped > 0)
        {
            _store.SaveWorkspaceDebounced(workspace);
        }

        if (dropped > 0)
        {
            // 조용히 삼키지 않는다 (§5).
            Warn($"배치에서 놓을 수 없는 타일 {dropped}개를 뺐다 (폴더가 없거나 격자를 벗어났다).");
        }

        _store.SaveFailed += OnSaveFailed;
    }

    public Workspace Workspace { get; }

    public string Title => string.IsNullOrWhiteSpace(Workspace.Title) ? "(제목 없음)" : Workspace.Title;

    public IReadOnlyList<FolderRowViewModel> Rows { get; }

    public int GridCols { get; }

    public int GridRows { get; }

    /// <summary>배치된 타일들. 순서는 <c>tiles[]</c> 그대로다.</summary>
    public ObservableCollection<TileViewModel> Tiles { get; }

    /// <summary>격자 전체 칸(4x4=16). 배치 편집 모드에서 경계선·빈 칸 과녁으로 쓴다.</summary>
    public ObservableCollection<GridCellViewModel> Cells { get; }

    /// <summary>타일들의 패널 — 새로고침·검색 등 기존 경로가 쓰는 평면 목록.</summary>
    public IReadOnlyList<FolderPanelViewModel> Panels { get; private set; } = [];

    /// <summary>순환1 칸의 패널. 순환 칸이 하나도 없으면 null.</summary>
    public FolderPanelViewModel? RotatingPanel => PrimaryRotatingTile?.Panel;

    /// <summary>마지막으로 왼쪽 리스트 클릭이 향한 순환 칸 — 상태 바가 이걸 따라간다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusPath))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private TileViewModel? activeRotatingTile;

    /// <summary>하단 상태 바 — 순환 칸의 전체 경로(헤더가 좁아 잘리므로, §3.2).</summary>
    public string StatusPath =>
        (ActiveRotatingTile ?? PrimaryRotatingTile)?.Panel.CurrentPath
        ?? "왼쪽 목록에서 폴더를 클릭하면 여기에 전체 경로가 나온다.";

    /// <summary>순환 칸이 여럿일 수 있으니 어느 칸의 경로인지 밝힌다.</summary>
    public string StatusLabel
    {
        get
        {
            var tile = ActiveRotatingTile ?? PrimaryRotatingTile;
            return tile is null ? "순환 칸:" : $"{tile.KindTag} 칸:";
        }
    }

    // ── 상태 바 알림 ─────────────────────────────────────────────────────────
    //
    // 두 축으로 나눈다.
    //   경고(빨강) 대 정보(보조색) — 무슨 색으로 보일지.
    //   사라짐 대 남음            — 스스로 지워질지.
    // "순환2 칸 없음 → 순환1" 같은 안내가 실패와 같은 빨강으로 계속 붙어 있으면
    // 정작 저장 실패·부분 실패를 놓친다. 반대로 실패 요약은 절대 사라지면 안 된다.
    // 사라지는 타이밍은 뷰(MainWindow)의 타이머가 잡는다 — 뷰모델은 상태만 갖는다.

    /// <summary>사용자에게 알릴 것 (§5 — 조용히 삼키지 않는다).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? message;

    /// <summary>빨강으로 띄울 것인가(실패·거절). 정보성 안내는 false.</summary>
    [ObservableProperty]
    private bool messageIsWarning;

    /// <summary>잠깐 떴다 스스로 사라지는가. 실패·부분 실패 요약은 false.</summary>
    [ObservableProperty]
    private bool messageIsTransient;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>정보성 안내 — 보조색, 잠깐 떴다 사라진다.</summary>
    private void Inform(string text)
    {
        Message = text;
        MessageIsWarning = false;
        MessageIsTransient = true;
    }

    /// <summary>
    /// 경고 — 빨강. 기본은 남는다(실패·부분 실패). 화면이 이미 답을 보여주는 거절
    /// (겹쳐서 못 놓음 등)은 <paramref name="transient"/> 로 스스로 지우게 한다.
    /// </summary>
    private void Warn(string text, bool transient = false)
    {
        Message = text;
        MessageIsWarning = true;
        MessageIsTransient = transient;
    }

    private void ClearMessage()
    {
        Message = null;
        MessageIsWarning = false;
        MessageIsTransient = false;
    }

    /// <summary>타이머가 부른다. 남아야 할 메시지는 건드리지 않는다.</summary>
    public void ExpireTransientMessage()
    {
        if (MessageIsTransient)
        {
            ClearMessage();
        }
    }

    /// <summary>겹쳐서 못 놓았다 — 드래그를 처리하는 뷰가 알린다.</summary>
    public void ReportTileOverlap() => Warn("그 자리에는 다른 타일이 있다.", transient: true);

    /// <summary>이미 상시 칸에 있는 폴더를 또 넣으려 했다 — 드래그를 막은 뷰도 부른다.</summary>
    public void RejectDuplicatePlacement(FolderRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Warn($"이미 상시 칸에 있다: {row.DisplayName}", transient: true);
    }

    private TileViewModel? PrimaryRotatingTile => Tiles
        .Where(t => t.IsRotating)
        .OrderBy(t => t.RotationIndex ?? int.MaxValue)
        .FirstOrDefault();

    private IEnumerable<TileSpec> Specs => Tiles.Select(t => t.Spec);

    // ── 배치 편집 모드 (Phase 2) ─────────────────────────────────────────────

    /// <summary>
    /// 켜면 타일을 옮기고 늘릴 수 있고, 파일 목록은 잠긴다. 평소(뷰 모드)엔 타일이 못박혀
    /// 파일 작업 중에 배치가 흔들리지 않는다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewMode))]
    [NotifyPropertyChangedFor(nameof(EditToggleText))]
    private bool isEditingLayout;

    /// <summary>파일 목록이 살아 있는가 — 편집 모드의 반대.</summary>
    public bool IsViewMode => !IsEditingLayout;

    public string EditToggleText => IsEditingLayout ? "배치 편집 끝내기" : "배치 편집";

    [RelayCommand]
    public void ToggleLayoutEdit()
    {
        IsEditingLayout = !IsEditingLayout;
        CancelPlace();

        // 조작 안내는 툴바가 편집 중 내내 띄우고 있다. 상태 바에 또 쓰면 두 번 읽힌다.
        ClearMessage();

        if (!IsEditingLayout)
        {
            Save();
        }
    }

    partial void OnIsEditingLayoutChanged(bool value)
    {
        foreach (var tile in Tiles)
        {
            tile.IsEditing = value;
            tile.IsDragging = false;
        }
    }

    // ── 타일 이동 · 리사이즈 ─────────────────────────────────────────────────

    /// <summary>그 자리에 놓을 수 있는가. 겹치면 false — 밀어내지 않는다.</summary>
    public bool CanPlaceTile(TileViewModel? tile, int cellX, int cellY, int spanX, int spanY) =>
        GridLayout.CanPlace(Specs, tile?.Spec, cellX, cellY, spanX, spanY, GridCols, GridRows);

    /// <summary>드래그 이동. 격자 밖이거나 겹치면 아무것도 하지 않고 false.</summary>
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

    /// <summary>모서리 리사이즈. 격자(4x4) 밖으로는 못 나가고 겹치면 거부한다.</summary>
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

    /// <summary>
    /// 타일을 그리드에서 뺀다. <b>폴더 등록은 남는다</b> — 왼쪽 리스트에서 계속 보이고
    /// 다시 빈 칸에 넣을 수 있다.
    /// </summary>
    [RelayCommand]
    public void RemoveTile(TileViewModel? tile)
    {
        if (tile is null || !Tiles.Contains(tile))
        {
            return;
        }

        var removedIndex = tile.IsRotating ? tile.RotationIndex : null;

        Workspace.Tiles!.Remove(tile.Spec);
        Tiles.Remove(tile);
        tile.Panel.Clear();

        var orphans = removedIndex is null ? 0 : DemoteFoldersTo(removedIndex.Value, 1);

        RefreshDerivedLayout();
        Save();

        Inform(removedIndex is null
            ? "타일을 뺐다. 폴더는 왼쪽 리스트에 남아 있다."
            : orphans > 0
                ? $"순환{removedIndex} 칸을 뺐다 — 그 번호를 쓰던 폴더 {orphans}개는 순환1로."
                : $"순환{removedIndex} 칸을 뺐다.");
    }

    /// <summary>
    /// 상시 ↔ 순환 토글. 순환 → 상시는 <b>지금 그 칸에 떠 있는 폴더</b>를 못박는다 —
    /// 빈 순환 칸이면 못박을 대상이 없으므로 거절하고 이유를 말한다.
    /// </summary>
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

    // ── 빈 칸에 폴더 넣기 (드래그 + 클릭) ────────────────────────────────────

    /// <summary>빈 칸 클릭으로 열리는 폴더 선택 목록이 떠 있는가.</summary>
    [ObservableProperty]
    private bool isFolderPickerOpen;

    /// <summary>선택을 기다리는 칸.</summary>
    [ObservableProperty]
    private GridCellViewModel? pendingCell;

    /// <summary>빈 칸 클릭 → 등록된 폴더 중에서 고른다.</summary>
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

    /// <summary>고른 폴더를 기다리던 칸에 상시 1x1 타일로 넣는다.</summary>
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

    /// <summary>기다리던 칸을 빈 순환 칸으로 만든다(번호는 비어 있는 가장 낮은 것).</summary>
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

    /// <summary>왼쪽 리스트에서 빈 칸으로 끌어다 놓기, 그리고 클릭 선택의 공통 종착지.</summary>
    public async Task<bool> PlaceFolderInCellAsync(FolderRowViewModel row, GridCellViewModel cell)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cell);

        if (!IsEditingLayout || !CanPlaceTile(null, cell.X, cell.Y, 1, 1))
        {
            return false;
        }

        // 같은 폴더를 상시 칸 둘에 두는 것은 16칸을 낭비할 뿐이다 — "겹침=거부"와 같은 결로 막는다.
        // (순환 칸은 폴더를 못박지 않으므로 이 규칙과 무관하다.)
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

        // 창을 열 때는 InitializeAsync 가 상시 패널을 채우지만, 편집 중에 새로 넣은 타일은
        // 아무도 읽어 주지 않는다. 넣자마자 내용이 보여야 한다(넣고 나서 빈 칸으로 남으면
        // 앱을 껐다 켜야 나타난다).
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

    // ── 순환 번호 지정 ───────────────────────────────────────────────────────

    private void ApplyRotationChoice(RotationChoiceViewModel choice)
    {
        // 1 은 기본값이라 null 로 눕힌다 — 파일에 쓸데없는 슬롯을 남기지 않는다.
        choice.Row.Entry.RotationSlot = choice.Slot <= 1 ? null : choice.Slot;
        choice.Row.NotifyRotationSlot();

        RefreshRotationChoices();
        Save();

        Inform($"{choice.Row.DisplayName} → {choice.Label} 칸");
    }

    // ── 복사 대상함 + 다중 선택 복사/이동 (§3.2 · §4.2 · Phase 3 Stage 2) ────

    /// <summary>목적지로 상주하는 폴더들. 체크된 것이 이번 실행의 목적지다.</summary>
    public ObservableCollection<TrayEntryViewModel> Tray { get; }

    public bool TrayIsEmpty => Tray.Count == 0;

    /// <summary>
    /// 지금 선택을 들고 있는 패널. 선택은 타일 하나에만 존재한다 (§4.8) —
    /// "현재 선택"이 어느 타일 것인지 흔들리면 복사가 엉뚱한 파일을 집는다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(OperationSummary))]
    [NotifyPropertyChangedFor(nameof(CanRunOperation))]
    private FolderPanelViewModel? selectionOwner;

    public bool HasSelection => SelectionOwner?.HasSelection == true;

    private IReadOnlyList<FileItemViewModel> Selection => SelectionOwner?.SelectedItems ?? [];

    private List<TrayEntryViewModel> CheckedDestinations => [.. Tray.Where(t => t.IsChecked)];

    /// <summary>실행하기 전에 "무엇 → 어디, 몇 건"을 미리 보여준다 (§4.7 안전장치).</summary>
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

            // 이동은 원본을 옮기는 것이라 목적지가 하나여야 한다. 누르기 전에 미리 말해 준다.
            return destinations > 1 ? text + " · 이동은 목적지 하나만" : text;
        }
    }

    public bool CanRunOperation => HasSelection && CheckedDestinations.Count > 0;

    /// <summary>왼쪽 리스트 폴더를 대상함에 넣는다 (§11 — 임의 경로 추가는 후순위).</summary>
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
        // 비면 슬롯 자체를 지운다 — 빈 배열을 파일에 남기지 않는다.
        Workspace.CopyTray = Tray.Count == 0 ? null : [.. Tray.Select(t => t.Entry.Id)];
        OnPropertyChanged(nameof(TrayIsEmpty));
        NotifyOperationState();
        Save();
    }

    /// <summary>체크가 바뀌면 요약과 버튼 상태를 다시 계산한다.</summary>
    public void NotifyOperationState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(OperationSummary));
        OnPropertyChanged(nameof(CanRunOperation));
        CopySelectionCommand.NotifyCanExecuteChanged();
        MoveSelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>패널이 선택을 얻거나 잃었다. 선택은 한 타일에만 남긴다 (§4.8).</summary>
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

    /// <summary>
    /// 파일 조작이 도는 중인가. 확인·결과 대화상자가 창을 껐다 켜면서 "창 활성화 시 재조회"
    /// (§8)를 불러 실행 중의 새로고침과 겹친다 — 같은 폴더를 두 번 읽을 뿐 아니라
    /// 열거가 서로를 취소하며 엉킨다. 조작이 끝나고 한 번만 읽는다.
    /// </summary>
    public bool IsOperationRunning { get; private set; }

    private async Task RunOperationAsync(FileOperationKind op)
    {
        var sources = Selection;
        var destinations = CheckedDestinations;

        if (sources.Count == 0 || destinations.Count == 0)
        {
            return;
        }

        var request = new FileOperationRequest(
            op,
            [.. sources.Select(i => new OperationItem(i.FullPath, i.IsDirectory))],
            [.. destinations.Select(t => t.Entry.Path)],
            ConflictPolicy.Overwrite);

        var verb = op == FileOperationKind.Copy ? "복사" : "이동";

        // 파일 하나가 두 곳에 동시에 있을 수는 없다. 그대로 실행하면 첫 목적지로 옮겨진 뒤
        // 나머지가 전부 "원본이 없다"로 실패한다 — 시도 자체를 막고 이유를 말한다.
        // (복사는 다중 목적지가 정상이다, §4.2.)
        if (op == FileOperationKind.Move && destinations.Count > 1)
        {
            Warn($"이동은 목적지 하나만 고를 수 있다 (지금 {destinations.Count}개). " +
                 "여러 곳에 두려면 복사를 쓴다.");
            return;
        }

        var preview = await _engine.PreviewAsync(request).ConfigureAwait(true);

        if (preview.TotalItemCount == 0)
        {
            Warn($"{verb}할 곳이 없다 — 체크한 목적지에 모두 닿지 못했다.");
            return;
        }

        IsOperationRunning = true;

        // 이동은 원본이 사라진다. 되돌릴 수 없으니 한 번 확인받는다 (§4.7 — move 는 확인 강제).
        if (op == FileOperationKind.Move
            && !_prompt.Confirm($"{verb} 확인", MoveWarning(preview, sources.Count)))
        {
            IsOperationRunning = false;
            Inform("이동을 취소했다.");
            return;
        }

        try
        {
            var report = await _engine.ExecuteAsync(request).ConfigureAwait(true);
            ReportOperation(verb, report);
        }
        finally
        {
            // 옮긴 뒤의 실제 상태를 보여준다 — 목록이 옛것이면 방금 한 일을 못 믿는다.
            await RefreshAllPanelsAsync().ConfigureAwait(true);
            IsOperationRunning = false;
        }
    }

    private static string MoveWarning(FileOperationPreview preview, int sourceCount)
    {
        var text = $"파일 {sourceCount}개를 폴더 {preview.PerDestination.Count}개로 옮긴다 " +
                   $"({preview.TotalItemCount}건).{Environment.NewLine}원본은 사라진다.";

        if (preview.TotalOverwriteCount > 0)
        {
            text += $"{Environment.NewLine}같은 이름 {preview.TotalOverwriteCount}건을 덮어쓴다.";
        }

        if (preview.SkippedDestinations.Count > 0)
        {
            text += $"{Environment.NewLine}닿지 못한 목적지 " +
                    $"{preview.SkippedDestinations.Count}개는 제외된다.";
        }

        return text;
    }

    /// <summary>결과 보고 = 성공 / 건너뜀 / 실패 + 이유 (§5).</summary>
    private void ReportOperation(string verb, FileOperationReport report)
    {
        var ok = report.Results.Count(r => r.Status == OperationItemStatus.Succeeded);
        var skipped = report.Results.Count(r => r.Status == OperationItemStatus.Skipped);
        var failed = report.Results.Count(r => r.Status == OperationItemStatus.Failed);
        var overwritten = report.Results.Count(r => r.Overwritten);

        var summary = $"{verb} — 성공 {ok} · 건너뜀 {skipped} · 실패 {failed}";
        if (overwritten > 0)
        {
            // 덮어쓰기는 차단하지 않되 일어났음을 반드시 알린다 (§4.3).
            summary += $" · 덮어씀 {overwritten}";
        }

        if (report.Canceled)
        {
            summary += " · 취소됨";
        }

        if (failed == 0 && skipped == 0)
        {
            Inform(summary);
            return;
        }

        // 성공하지 못한 것이 있으면 요약은 상태 바에 남기고, 사유는 한 줄씩 따로 보여준다.
        Warn(summary);

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

    // ── 초기화 · 갱신 ────────────────────────────────────────────────────────

    /// <summary>창이 열릴 때 한 번. 상시 패널을 병렬로 채우고 접근 가능 여부를 확인한다.</summary>
    public async Task InitializeAsync()
    {
        Workspace.LastUsed = DateTimeOffset.Now;
        _store.SaveWorkspaceDebounced(Workspace);

        var loads = Panels
            .Where(p => p.Entry is not null)
            .Select(p => p.GoToAnchorAsync());

        // 접근 불가 표시는 열거를 기다리지 않고 따로 확인한다. 죽은 네트워크 경로 하나가
        // 몇 초를 잡아먹어도 나머지 UI 는 이미 떠 있다.
        var probes = Rows.Select(ProbeRowAsync);

        await Task.WhenAll(loads.Concat(probes)).ConfigureAwait(true);
        UpdateRowStates();
    }

    /// <summary>
    /// 왼쪽 리스트 클릭 = 그 폴더의 순환 번호 칸에 표시. 같은 폴더 재클릭 = 앵커로 리셋 (§4.1).
    /// <para>
    /// 단, <b>그 폴더가 이미 상시 칸에 못박혀 있으면 순환 칸을 건드리지 않는다</b> —
    /// 화면에 이미 떠 있는 것을 순환 칸에 또 띄우면 그 한 자리를 헛되이 쓴다. 대신 그
    /// 상시 칸을 앵커로 되돌린다(하위로 들어가 있던 상시 칸을 집으로 부르는 용도).
    /// </para>
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ShowFolderAsync(FolderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // 편집 중에 파일 목록이 바뀌면 무엇을 옮기는 중인지 헷갈린다.
        if (IsEditingLayout)
        {
            return;
        }

        if (PinnedTileFor(row) is { } pinned)
        {
            // 앵커 밖에 있었으면 되돌아오는 것 자체가 응답이다. 이미 앵커에 있었다면
            // 아무 일도 안 일어난 것처럼 보이므로 왜 그런지 한 줄로 말해 준다.
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

        // 지정한 번호의 칸이 없으면 폴백한다 — 클릭이 아무 일도 안 하는 것보다 낫고,
        // 어디로 갔는지 한 줄로 말해 준다.
        if (tile.RotationIndex == wanted)
        {
            ClearMessage();
        }
        else
        {
            Inform($"순환{wanted} 칸 없음 → {tile.KindTag}");
        }

        ActiveRotatingTile = tile;

        // 성격 무관 — 상시 폴더를 눌러도 순환 칸에 뜬다(§3.2).
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

    /// <summary>이 폴더를 못박고 있는 상시 칸. 없으면 null.</summary>
    public TileViewModel? PinnedTileFor(FolderRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return Tiles.FirstOrDefault(t => t.IsPinned && t.Spec.FolderId == row.Entry.Id);
    }

    /// <summary>
    /// 그 번호의 순환 칸. 없으면 순환1, 그것도 없으면 남은 것 중 가장 낮은 번호로 폴백한다.
    /// </summary>
    public TileViewModel? ResolveRotatingTile(int slot)
    {
        var rotating = Tiles.Where(t => t.IsRotating).ToList();

        return rotating.FirstOrDefault(t => t.RotationIndex == slot)
               ?? rotating.FirstOrDefault(t => t.RotationIndex == 1)
               ?? rotating.OrderBy(t => t.RotationIndex ?? int.MaxValue).FirstOrDefault();
    }

    /// <summary>수동 갱신 <c>F5</c> + 창 활성화 시 재조회 (§8). watcher 는 뒤 Phase.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAllAsync()
    {
        ClearMessage();

        await Task.WhenAll(Panels.Where(p => p.Entry is not null).Select(p => p.RefreshAsync()))
            .ConfigureAwait(true);
        await Task.WhenAll(Rows.Select(ProbeRowAsync)).ConfigureAwait(true);

        UpdateRowStates();
    }

    /// <summary>종료 직전 — 밀린 디바운스 저장을 마무리한다.</summary>
    public void Shutdown()
    {
        _store.SaveFailed -= OnSaveFailed;
        _store.Flush();
    }

    // ── 왼쪽 컬럼 접기 (§3.2). 사용자 설정이라 워크스페이스에 저장한다. ──

    /// <summary>위쪽 폴더 리스트 접힘.</summary>
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

    /// <summary>아래쪽 대상함/매크로 탭 영역 접힘.</summary>
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

    /// <summary>둘 중 하나라도 접히면 사이 스플리터는 의미가 없다.</summary>
    public bool ShowLeftSplitter => !FoldersCollapsed && !LowerCollapsed;

    [RelayCommand]
    public void ToggleFolders() => FoldersCollapsed = !FoldersCollapsed;

    [RelayCommand]
    public void ToggleLower() => LowerCollapsed = !LowerCollapsed;

    /// <summary>v1 파일에는 슬롯이 없다. 처음 접을 때 만든다(저장하면 최신 버전으로 승격된다).</summary>
    private LeftLayoutSpec LeftLayout() => Workspace.LeftLayout ??= new LeftLayoutSpec();

    private void SaveLeftLayout()
    {
        OnPropertyChanged(nameof(ShowLeftSplitter));
        Save();
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private TileViewModel CreateTile(TileSpec spec)
    {
        var panel = new FolderPanelViewModel(
            _enumerator, _shell, _clipboard, spec.Kind == TileKind.Rotating, ReportError);

        if (spec.Kind == TileKind.Pinned && spec.FolderId is { } id)
        {
            panel.Entry = Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry;
        }

        return new TileViewModel(spec, panel) { IsEditing = IsEditingLayout };
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

    /// <summary>지운/바뀐 순환 번호를 가리키던 폴더들을 다른 번호로 내린다. 반환값은 옮긴 수.</summary>
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

    /// <summary>타일이 바뀔 때마다 파생 상태(패널 목록·빈 칸·순환 선택지)를 다시 세운다.</summary>
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
    }

    /// <summary>
    /// 폴더 행의 "순환 칸 지정" 목록. 실제로 있는 순환 번호 + (필요하면) 지금 지정된 번호.
    /// </summary>
    private void RefreshRotationChoices()
    {
        var existing = Tiles
            .Where(t => t.IsRotating)
            .Select(t => t.RotationIndex ?? 1)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        var showTag = existing.Count > 1;

        // 상시 칸에 못박힌 폴더는 클릭해도 순환 칸으로 가지 않는다 — 그 행에 순환 번호를
        // 달아 두면 거짓말이 된다. 타일을 빼면 다시 붙는다.
        var pinnedFolders = Tiles
            .Where(t => t.IsPinned && t.Spec.FolderId is not null)
            .Select(t => t.Spec.FolderId!.Value)
            .ToHashSet();

        var pinnedFlagChanged = false;

        foreach (var row in Rows)
        {
            row.HasPinnedTile = pinnedFolders.Contains(row.Entry.Id);
            row.ShowRotationTag = showTag && !row.HasPinnedTile;

            // folders[].pinned 는 이제 상시 타일의 그림자다. 런처 카드의 칩과 v1/v2 배치
            // 유도가 이 필드를 보므로, 배치를 바꿔 놓고 플래그만 옛날 값이면 둘이 어긋난다.
            if (row.Entry.Pinned != row.HasPinnedTile)
            {
                row.Entry.Pinned = row.HasPinnedTile;
                pinnedFlagChanged = true;
            }

            // 상시 칸에 못박힌 폴더는 클릭해도 순환 칸으로 가지 않는다 — 번호를 고르게 두면
            // 아무 일도 일으키지 않는 설정을 만지게 된다. 선택지를 아예 주지 않는다.
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

        if (pinnedFlagChanged)
        {
            Save();
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

    /// <summary>패널 상태를 왼쪽 리스트 행에 반영한다 (§3.2 — 행에도 하위 경로 상태를 표시).</summary>
    private void UpdateRowStates()
    {
        foreach (var row in Rows)
        {
            // 같은 폴더가 상시 칸과 순환 칸에 동시에 떠 있을 수 있다.
            // 행이 컨트롤하는 건 순환 칸이므로 그쪽을 우선 반영한다.
            var rotating = Tiles.FirstOrDefault(
                t => t.IsRotating && ReferenceEquals(t.Panel.Entry, row.Entry));
            var panel = rotating?.Panel
                        ?? Panels.FirstOrDefault(p => ReferenceEquals(p.Entry, row.Entry));

            row.IsShowingInRotating = rotating is not null;
            row.LocationHint = panel?.LocationHint;

            if (panel?.Failure is not null)
            {
                row.IsInaccessible = true;
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
}
