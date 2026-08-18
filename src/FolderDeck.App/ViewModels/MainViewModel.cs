using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Services;
using FolderDeck.App.Watching;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Paths;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.ViewModels;

/// <summary>메인 창 (§3.2) — 좌우 2단.</summary>
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
    private readonly ISelfLauncher _selfLauncher;

    /// <summary>디바운스 저장 실패는 스레드 풀에서 올라온다 — UI 스레드로 되돌리기 위해 잡아 둔다.</summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>타일마다 감시자 하나 (§8 · Phase 5). 타일이 빠지거나 창이 닫힐 때 푼다.</summary>
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
        ISelfLauncher selfLauncher)
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

        Macros = [.. (workspace.Macros ?? []).Select(m => new MacroCardViewModel(m))];

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

    public ObservableCollection<FolderRowViewModel> Rows { get; }

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

    // ── 인스턴스 관리 (§6.2 · Phase 4 Stage 1) ────────────────────────────────

    /// <summary>
    /// 이미 열려 있는 창에 신호를 보내지 못해(좀비) 이 창이 새로 떴다 (§3.2-3).
    /// 같은 작업 관리가 두 창에 떠 있을 수 있다는 것을 밝히고, <b>사라지지 않는</b> 경고로 남긴다.
    /// </summary>
    public void ReportDuplicateWindow() => Warn(
        "이미 열려 있는 창에 신호를 보내지 못해 이 창을 새로 열었다. " +
        "같은 작업 관리가 두 창에 떠 있을 수 있다 — 한쪽을 닫는 편이 좋다.");

    /// <summary>
    /// 이 창이 "앞으로" 신호를 받을 수 없다. 다시 열면 새 창이 뜨므로 조용히 넘기지 않는다 (§5).
    /// </summary>
    public void ReportNoSignal(string detail) => Warn(
        $"이 창은 '창 보이기' 신호를 받지 못한다 (파이프를 열지 못했다: {detail}). " +
        "이 작업 관리를 다시 열면 창이 하나 더 뜬다.");

    /// <summary>
    /// 런처를 띄운다 (§5). 자동 열기(<c>skipLauncher</c>)가 켜져 있으면 인자 없는 실행이
    /// 곧바로 워크스페이스로 가버리므로, 다른 작업 관리로 가는 길이 이 버튼이다.
    /// </summary>
    /// <remarks>
    /// <c>--launcher</c> 로 자기 자신을 띄운다. 런처가 이미 떠 있으면 그 프로세스가 §4.3 대로
    /// 기존 런처를 앞으로 보내고 스스로 사라진다 — 여기서 따로 판단하지 않는다.
    /// </remarks>
    [RelayCommand]
    public void OpenLauncher()
    {
        var error = _selfLauncher.OpenLauncher();
        if (error is not null)
        {
            Warn($"런처를 띄우지 못했다: {error}");
            return;
        }

        Inform("런처를 띄웠다.");
    }

    // ── 창 위치 (§6.1 · Phase 4 Stage 2) ─────────────────────────────────────

    /// <summary>
    /// 창을 옮기거나 크기를 바꿀 때마다 뷰가 부른다. 값이 실제로 달라졌을 때만 §5 의
    /// "변경 즉시 저장(디바운스)"에 태운다 — 이벤트는 끄는 동안 수십 번 온다.
    /// </summary>
    /// <param name="bounds">
    /// <b>복원 크기</b> (DIP). 최대화 상태여도 최대화된 사각형이 아니라 풀었을 때 돌아갈
    /// 자리를 넘긴다 — 최대화된 좌표를 저장하면 돌아갈 자리가 없어진다.
    /// </param>
    public void UpdateWindowPlacement(ScreenRect bounds, bool maximized)
    {
        if (!bounds.IsUsable)
        {
            // 최소화 중이거나 창이 아직 자리를 못 잡았다. 쓰레기 좌표를 저장하지 않는다.
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

    /// <summary>
    /// 저장된 자리로 못 띄우고 기본 위치로 갔다 (§6.1 화면 밖 방어). 실패가 아니므로
    /// <b>정보</b> 등급(§5.1)이다 — 다만 "내가 둔 자리가 아닌데" 하는 순간은 없앤다.
    /// </summary>
    public void ReportWindowMoved() =>
        Inform("저장해 둔 창 자리를 지금 화면에서 되찾을 수 없어 기본 위치로 띄웠다.");

    /// <summary>
    /// 작업영역이 최소 크기보다 작아 더 줄이지 않았다 (§6.1). 창이 화면을 넘지만 제목줄은
    /// 잡히므로 실패가 아니다 — 위 둘과 같은 <b>정보</b> 등급(§5.1).
    /// </summary>
    public void ReportWindowAtMinimum() =>
        Inform("지금 화면이 좁아 창을 최소 크기로 띄웠다 — 화면 밖으로 조금 넘칠 수 있다.");

    /// <summary>저장된 크기가 지금 작업영역보다 커서 줄였다 (§6.1). 역시 정보 등급.</summary>
    public void ReportWindowResized() =>
        Inform("저장해 둔 창 크기가 지금 화면보다 커서 화면에 맞춰 줄였다.");

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
        DetachWatcher(tile);

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

    public bool CanRunOperation => HasSelection && CheckedDestinations.Count > 0 && !IsOperationRunning;

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
        OnPropertyChanged(nameof(CanTrashSelection));
        OnPropertyChanged(nameof(CanCreateMacro));
        CopySelectionCommand.NotifyCanExecuteChanged();
        MoveSelectionCommand.NotifyCanExecuteChanged();
        TrashSelectionCommand.NotifyCanExecuteChanged();
        CreateMacroCommand.NotifyCanExecuteChanged();
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
    /// <para>도는 동안에는 다음 조작(버튼·드롭·휴지통)을 받지 않는다.</para>
    /// </summary>
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

        // 파일 하나가 두 곳에 동시에 있을 수는 없다. 그대로 실행하면 첫 목적지로 옮겨진 뒤
        // 나머지가 전부 "원본이 없다"로 실패한다 — 시도 자체를 막고 이유를 말한다.
        // (복사는 다중 목적지가 정상이다, §4.2.)
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

    /// <summary>
    /// 미리보기 → (필요하면) 물어보기 → 실행. 대상함 버튼과 드래그드롭이 공유한다.
    /// </summary>
    /// <param name="confirmMove">
    /// 겹치는 것이 없을 때 이동 확인창을 띄울지. 대상함 버튼은 띄우고 드래그는 띄우지 않는다
    /// (§4.2-2 — 손으로 과녁을 겨눈 한 번의 동작이다).
    /// </param>
    /// <param name="sameFolderSkipped">
    /// 엔진에 넘기기 전에 ①(온 곳과 같은 폴더)로 빠진 건수 (§4.9). 엔진의 보고에는 없으므로
    /// 결과 문구까지 따로 들고 간다.
    /// </param>
    /// <param name="verbOverride">
    /// 사용자에게 보일 이름. 기본은 동작 그대로(복사/이동)이고, <b>같은 <c>Copy</c> 인데 이름이
    /// 달라야 하는 자리</b>만 이걸 준다 — 붙여넣기가 그렇다(§4.6 322~323행). 확인창·진행 줄·
    /// 결과 요약이 전부 이 낱말을 쓴다.
    /// </param>
    private async Task StartOperationAsync(
        FileOperationKind op,
        IReadOnlyList<OperationItem> items,
        IReadOnlyList<string> destinations,
        bool confirmMove,
        int sameFolderSkipped = 0,
        string? verbOverride = null)
    {
        var verb = verbOverride ?? (op == FileOperationKind.Copy ? "복사" : "이동");

        // 겹치는 건수는 <b>정책과 무관하게</b> 세야 한다. 엔진은 덮어쓰기일 때만 세므로
        // (건너뛰기·이름바꾸기는 덮어쓰지 않으니까) 여기서는 항상 덮어쓰기로 훑는다.
        var preview = await _engine
            .PreviewAsync(new FileOperationRequest(op, items, destinations, ConflictPolicy.Overwrite))
            .ConfigureAwait(true);

        if (preview.TotalItemCount == 0)
        {
            // 대상함 버튼과 드롭이 함께 쓰는 문구다 — "체크한 목적지"라고 하면 드롭에는 거짓말이 된다.
            Warn($"{verb}할 곳이 없다 — 목적지에 닿지 못했다.");
            return;
        }

        IsOperationRunning = true;

        var policy = ConflictPolicy;

        if (preview.TotalOverwriteCount > 0 && AskOnConflict)
        {
            // 겹칠 때만 묻는다. 이동이면 "원본은 사라진다"까지 이 창 하나에 담는다 —
            // 확인창과 충돌창이 잇달아 뜨면 두 번 읽고 두 번 눌러야 한다.
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
            // 이동은 원본이 사라진다. 되돌릴 수 없으니 한 번 확인받는다 (§4.7).
            IsOperationRunning = false;
            Inform("이동을 취소했다.");
            return;
        }

        await RunGuardedAsync(
            new FileOperationRequest(op, items, destinations, policy),
            verb,
            sameFolderSkipped: sameFolderSkipped).ConfigureAwait(true);
    }

    /// <summary>
    /// 세 진입점(버튼 · 드래그드롭 · 휴지통)이 공유하는 실행 껍데기 — 잠금, 진행·취소,
    /// 결과 보고, 끝난 뒤 목록 갱신.
    /// </summary>
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
            // 옮긴 뒤의 실제 상태를 보여준다 — 목록이 옛것이면 방금 한 일을 못 믿는다.
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
            // 진행 줄은 결과를 알리기 <b>전에</b> 접는다 — 부분 실패 대화상자 뒤에 다 끝난
            // 진행 막대가 남아 있으면 아직 도는 중인지 끝난 건지 알 수 없다.
            _progressRevealPending = false;
            _operationCts = null;
            IsProgressVisible = false;
        }

        ReportOperation(verb, report, successMessage, sameFolderSkipped);
    }

    // ── 매크로 (§4.7 · Phase 3 Stage 5) ──────────────────────────────────────
    //
    // 대상함·드래그와 <b>같은 엔진</b>에 얹힌 사용자 정의 규칙이다. 여기서 하는 일은
    // "저장된 규칙을 지금 화면 상태로 풀어(resolve) 요청 하나를 만드는 것"뿐이다.

    /// <summary>저장된 규칙 카드들 (§3.2 매크로 탭).</summary>
    public ObservableCollection<MacroCardViewModel> Macros { get; }

    public bool MacrosAreEmpty => Macros.Count == 0;

    /// <summary>지금 화면 상태로 매크로를 만들 수 있는가 — 고른 것이 있어야 한다.</summary>
    public bool CanCreateMacro => HasSelection && !IsOperationRunning;

    /// <summary>
    /// 방금 손으로 한 조작을 규칙으로 굳힌다. 창은 <b>지금 상태로 미리 채워져</b> 뜨므로
    /// 대개는 이름만 쓰고 확인을 누르게 된다.
    /// </summary>
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

        if (_macroEditor.Edit(suggested, canFixPath: selection.Count == 1) is not { } draft)
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

            // move·trash 는 확인을 끄지 못한다 (§4.7).
            Confirm = draft.Confirm || draft.Op != FileOperationKind.Copy,
        };

        if (draft.Op != FileOperationKind.Trash)
        {
            definition.Dest = new MacroDest
            {
                Kind = draft.DestKind,

                // "지금 체크된 폴더"는 만드는 순간에 굳힌다 — 나중에 체크가 바뀌어도
                // 이 규칙이 가리키는 곳은 그대로여야 한다.
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

    [RelayCommand]
    public void RemoveMacro(MacroCardViewModel? card)
    {
        if (card is null || !Macros.Contains(card))
        {
            return;
        }

        Workspace.Macros?.Remove(card.Definition);
        Macros.Remove(card);

        // 비면 슬롯 자체를 지운다 — 빈 배열을 파일에 남기지 않는다.
        if (Workspace.Macros is { Count: 0 })
        {
            Workspace.Macros = null;
        }

        NotifyMacros();
        Save();
        Inform($"매크로를 지웠다: {card.Name}");
    }

    /// <summary>저장된 규칙을 지금 화면 상태로 풀어 실행한다 (§4.7).</summary>
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

        // 이동은 목적지가 하나여야 한다 (§4.2) — 규칙이라고 예외가 아니다.
        if (macro.Op == FileOperationKind.Move && destinations.Count > 1)
        {
            Warn($"'{card.Name}' — 이동인데 목적지가 {destinations.Count}개다. " +
                 "여러 곳에 두려면 복사로 바꾼다.");
            return;
        }

        // 겹치는 건수는 정책과 무관하게 세야 하므로 항상 덮어쓰기로 훑는다.
        var preview = await _engine
            .PreviewAsync(new FileOperationRequest(
                macro.Op, items, destinations, ConflictPolicy.Overwrite))
            .ConfigureAwait(true);

        if (preview.TotalItemCount == 0)
        {
            Warn($"'{card.Name}' — {verb}할 곳이 없다. 목적지에 닿지 못했다.");
            return;
        }

        IsOperationRunning = true;

        // 실행 전 미리보기가 기본이다 (§4.7). move·trash 는 끄지 못한다.
        if ((macro.Confirm || macro.Op != FileOperationKind.Copy)
            && !_prompt.Confirm($"매크로 실행 — {card.Name}", MacroPreviewText(macro, preview, items)))
        {
            IsOperationRunning = false;
            Inform($"'{card.Name}' 실행을 취소했다.");
            return;
        }

        // 매크로는 <b>자기 정책</b>을 쓴다 — 워크스페이스의 "겹치면 묻기"(§4.3)는 직접 조작용이다.
        // 반복 실행이 존재 이유인데 매번 물으면 자동화가 아니다. 대신 미리보기가 건수를 미리 알린다.
        await RunGuardedAsync(
            new FileOperationRequest(macro.Op, items, destinations, macro.OnConflict), verb)
            .ConfigureAwait(true);
    }

    /// <summary>규칙의 대상을 지금 상태로 푼다. 없으면 빈 목록.</summary>
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

            // 사라진 경로는 여기서 걸러 낸다 — 엔진까지 보내면 "원본이 없다" 한 줄로 끝나서
            // 규칙이 고장난 것인지 이번만 없는 것인지 구분이 안 된다.
            return isDirectory || System.IO.File.Exists(path)
                ? [new OperationItem(path, isDirectory)]
                : [];
        }

        return [.. Selection.Select(i => new OperationItem(i.FullPath, i.IsDirectory))];
    }

    /// <summary>규칙의 목적지를 지금 상태로 푼다. 휴지통은 목적지가 없다.</summary>
    private List<string> ResolveMacroDestinations(MacroDefinition macro)
    {
        if (macro.Op == FileOperationKind.Trash || macro.Dest is null)
        {
            return [];
        }

        var paths = macro.Dest.Kind switch
        {
            // 앵커가 아니라 <b>화면에 뜬 그대로</b>가 대상이다 (§4.7) — 미리보기에 경로가 다 찍힌다.
            MacroDestKind.AllVisible => Panels
                .Where(p => p.Entry is not null && p.CurrentPath is not null)
                .Select(p => p.CurrentPath!),

            MacroDestKind.FolderIds => (macro.Dest.FolderIds ?? [])
                .Select(id => Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry.Path)
                .Where(p => p is not null)
                .Select(p => p!),

            _ => CheckedDestinations.Select(t => t.Entry.Path),
        };

        // 같은 폴더가 두 타일에 떠 있을 수 있다. 같은 곳에 두 번 넣지 않는다.
        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>"무엇 → 어디, N건, 덮어쓸 것 M건 + 목적지별 내역" (§4.7 안전장치).</summary>
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

    // ── 동일 이름 충돌 (§4.3 · Phase 3 Stage 4) ──────────────────────────────
    //
    // 엔진은 Stage 1 부터 세 정책을 다 할 줄 알았다(덮어쓰기 / 건너뛰기 / 둘 다 두기).
    // 여기서 하는 일은 "언제 물어보고 무엇을 기억할지"뿐이다.

    /// <summary>지금 쓰는 정책. 아무것도 안 정했으면 덮어쓰기 (§4.3).</summary>
    public ConflictPolicy ConflictPolicy => Workspace.OnConflict ?? ConflictPolicy.Overwrite;

    /// <summary>겹칠 때 물어보는가. 슬롯이 없는 구파일도 <b>묻는 쪽</b>이 기본이다.</summary>
    public bool AskOnConflict => Workspace.AskOnConflict ?? true;

    /// <summary>대상함에 한 줄로 보이는 지금 상태.</summary>
    public string ConflictPolicyText => AskOnConflict
        ? "같은 이름이 있으면 물어본다."
        : $"같은 이름이 있으면 {PolicyVerb(ConflictPolicy)} (묻지 않음).";

    /// <summary>"다시 묻기" 링크를 보일지 — 묻지 않기로 해 둔 상태에서만.</summary>
    public bool CanRestoreConflictPrompt => !AskOnConflict;

    private static string PolicyVerb(ConflictPolicy policy) => policy switch
    {
        ConflictPolicy.Skip => "건너뛴다",
        ConflictPolicy.Rename => "둘 다 둔다",
        _ => "덮어쓴다",
    };

    /// <summary>
    /// "다음부터 묻지 않기"를 되돌린다. 묻지 않기로 해 둔 것을 잊고 조용히 덮어쓰는 일이
    /// 없도록, 상태와 되돌릴 자리를 대상함에 늘 보여 둔다.
    /// </summary>
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
        // 기본값(덮어쓰기)은 슬롯을 남기지 않는다 — rotationSlot 과 같은 결.
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

    /// <summary>충돌 창 본문 — 몇 건이 겹치는지, 이동이면 원본이 사라진다는 것까지.</summary>
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

    // ── 진행 · 취소 (§1.1 — 폴더 재귀·볼륨 간 이동은 오래 걸린다) ────────────

    /// <summary>
    /// 진행 줄을 띄우기까지 기다리는 시간. 낱개 파일 몇 개는 눈 깜짝할 사이에 끝나는데
    /// 그때마다 막대가 번쩍이면 소음이다 — 오래 걸리는 것만 드러낸다.
    /// </summary>
    private const int ProgressRevealDelayMs = 400;

    /// <summary>지금 도는 조작의 취소 손잡이. 도는 것이 없으면 null.</summary>
    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// 진행 줄 예약이 아직 유효한가. 실행이 끝나면 내려서 <b>늦게 도착한 예약을 무시</b>한다.
    /// </summary>
    /// <remarks>
    /// <see cref="IsOperationRunning"/> 으로 판단하면 안 된다 — 조작이 눈 깜짝할 사이에 끝나고
    /// 결과 대화상자(모달)가 뜬 상태에서 0.4초가 지나면, 그 플래그는 아직 켜져 있어서
    /// <b>다 끝난 진행 막대가 대화상자 뒤에 되살아난다</b>(실제로 그랬다).
    /// </remarks>
    private bool _progressRevealPending;

    /// <summary>진행 줄이 보이는가. <see cref="IsOperationRunning"/> 보다 늦게 켜진다.</summary>
    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private string? progressText;

    /// <summary>0~100. 단위(대상 하나 × 목적지 하나) 기준이다.</summary>
    [ObservableProperty]
    private double progressValue;

    /// <summary>
    /// 취소. <b>손댄 것은 남고 시작 안 한 것은 안 한 채로</b> 결과에 실린다 — 엔진이
    /// 취소를 예외가 아니라 결과로 돌려준다. 이동은 복사가 전부 끝난 뒤에만 원본을 지우므로
    /// 중간 취소로 원본이 반쯤 사라지는 일은 없다.
    /// </summary>
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

        // 그새 끝났으면 띄우지 않는다.
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

        // 큰 폴더 하나는 단위로 세면 0/1 에 멈춰 보인다 — 실제로 만진 파일 수를 같이 보인다.
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

    /// <summary>
    /// 진행 보고를 솎아 UI 스레드로 넘긴다.
    /// </summary>
    /// <remarks>
    /// 엔진은 파일 하나마다 보고한다 — 파일 만 개짜리 폴더면 만 번이고, 그대로 디스패처에
    /// 던지면 진행 표시가 조작 자체보다 느려진다. 단위가 바뀌었을 때와 100ms 마다만 넘긴다.
    /// </remarks>
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
            // 엔진은 한 조작을 한 스레드로 돈다 — 여기 상태에 경합이 없다.
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

    // ── 드래그앤드롭 (§4.2-2 · Phase 3 Stage 3) ──────────────────────────────

    /// <summary>
    /// 이 자리에 놓을 수 있는가. 놓을 수 없으면 과녁을 켜지 않는다 — 끌고 가서 거절당하는
    /// 것보다 처음부터 받지 않는 편이 낫다(Phase 2 의 "겹침=거부"와 같은 결).
    /// </summary>
    public bool CanDropOnto(FolderEntry? destination, FileDropPayload? payload) =>
        destination is not null && payload is not null
        && CanDropOnto(destination.Path, payload.Items);

    /// <summary>
    /// 같은 판정을 <b>목적지 경로 + 항목 목록</b>으로만 한다 — 내부 드래그(§4.2-2)와 셸
    /// 드롭(§4.9)이 이 함수 하나를 쓴다.
    /// </summary>
    /// <remarks>
    /// 뷰모델 타입(<see cref="FolderEntry"/>·<see cref="FileDropPayload"/>)에 묶지 않는 이유:
    /// 셸 드롭이 타일에 떨어지면 목적지는 타일의 <c>CurrentPath</c> 이고 그 자리에는
    /// <see cref="FolderEntry"/> 가 없다(§4.9 — 앵커가 아니라 보고 있는 경로가 목적지다).
    /// <para>
    /// ③ 은 여기서 본다. <b>배치 편집 중</b>도 같이 막는다 — 편집 중에는 파일 목록이
    /// 잠기므로(§3.2) 타일은 애초에 히트테스트에서 빠지지만, 왼쪽 폴더 행과 대상함은 편집
    /// 중에도 살아 있어 셸 드롭이 들어올 수 있다. 세 자리를 같은 규칙으로 막는다.
    /// </para>
    /// </remarks>
    public bool CanDropOnto(string? destinationPath, IReadOnlyList<OperationItem>? items) =>
        destinationPath is not null && items is not null
        && !IsOperationRunning && !IsEditingLayout
        && AcceptedForDrop(destinationPath, items).Count > 0;

    /// <summary>
    /// 이 목적지가 실제로 받을 항목들 (§4.9).
    /// </summary>
    /// <remarks>
    /// <b>①(온 곳과 같은 폴더)의 판정 단위는 뭉치가 아니라 항목이다.</b> 앱 안의 드래그는
    /// 뭉치의 출처가 패널 하나여서(§4.8) 한 번만 보면 됐지만, 셸 드롭에는 그 전제가 없다
    /// (탐색기 검색 결과처럼 서로 다른 폴더의 항목이 한 번에 온다). 그래서 온 곳이 목적지와
    /// 같은 <b>그 항목만</b> 빼고 나머지는 받는다 — 뭉치째 거절하지 않는다. 남은 것이
    /// 하나라도 있으면 받고, 0개면 받지 않는다(= 과녁을 켜지 않는다).
    /// <para>
    /// 내부 드래그의 답은 이 변경으로 달라지지 않는다: 모든 항목의 부모가 같으므로 전부
    /// 빠져 0개가 되고, 예전의 뭉치 단위 거절과 같은 결론이 된다.
    /// </para>
    /// <para>
    /// ②(폴더를 자기 자신이나 자기 하위로)는 <b>뭉치째 거절</b>이다 — 그대로 두었다. 목적지를
    /// 삼키는 폴더가 하나라도 섞여 있으면 빈 목록이 된다.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<OperationItem> AcceptedForDrop(
        string destinationPath, IReadOnlyList<OperationItem> items)
    {
        // ② 폴더를 자기 자신이나 자기 하위로 넣으면 무한히 자란다.
        if (items.Any(i => i.IsDirectory
                           && (SamePath(destinationPath, i.Path) || IsUnder(destinationPath, i.Path))))
        {
            return [];
        }

        // ① 항목이 온 곳이 목적지와 같으면 무동작이다 — 그 항목만 뺀다.
        return [.. items.Where(i => !SamePath(destinationPath, ParentFolder(i.Path)))];
    }

    /// <summary>
    /// 항목이 온 폴더. 부모가 없는 루트면 자기 자신을 돌려준다 — 루트에 놓는 경우는
    /// ② 가 먼저 잡으므로 ① 의 답이 뒤집히지 않는다.
    /// </summary>
    private static string ParentFolder(string path) =>
        System.IO.Path.GetDirectoryName(path) is { Length: > 0 } parent ? parent : path;

    /// <summary>
    /// 끌어다 놓기 (§4.2-2). <b>기본은 이동, <c>Ctrl</c> 을 누르면 복사</b> — 탐색기 관용이다.
    /// 판정은 <b>놓는 순간</b>의 수정키로 한다(끌기 시작할 때가 아니라).
    /// </summary>
    /// <remarks>
    /// 대상함 버튼의 이동과 달리 <b>확인창을 띄우지 않는다</b>. 드래그는 손으로 과녁을 겨눈
    /// 한 번의 동작이라 "무엇을 어디로"가 이미 눈앞에 있고, 매번 대화상자가 뜨면 즉흥적인
    /// 단건 이동이라는 이 경로의 존재 이유가 없어진다. 대신 결과를 상태 바에 반드시 적는다.
    /// <para>
    /// 다만 <b>같은 이름이 겹치면 여기서도 묻는다</b>(§4.3) — 그때는 멈춰야 할 이유가 실제로
    /// 있고, 안 겹치는 대부분의 드롭은 여전히 창 없이 지나간다.
    /// </para>
    /// </remarks>
    public Task DropOntoFolderAsync(FolderEntry destination, FileDropPayload payload, bool copy) =>
        DropOntoPathAsync(destination.Path, payload.Items, copy);

    /// <summary>
    /// 경로 하나로 받는 진입점 — 내부 드래그(§4.2-2)와 셸 드롭(§4.9)이 여기서 만난다.
    /// <paramref name="copy"/> 의 <b>기본값 규칙은 부르는 쪽이 정한다</b>: 내부는 기본 이동
    /// (<c>Ctrl</c> 복사), 셸은 기본 복사(<c>Shift</c> 이동)로 <b>일부러 반대다</b>(§4.9).
    /// </summary>
    public async Task DropOntoPathAsync(
        string destinationPath, IReadOnlyList<OperationItem> items, bool copy)
    {
        if (!CanDropOnto(destinationPath, items))
        {
            return;
        }

        // ① 로 빠진 것은 엔진까지 가지 않는다. 몇 개가 빠졌는지는 결과에 적어야 한다(§4.9).
        var accepted = AcceptedForDrop(destinationPath, items);

        await StartOperationAsync(
            copy ? FileOperationKind.Copy : FileOperationKind.Move,
            accepted,
            [destinationPath],
            confirmMove: false,
            sameFolderSkipped: items.Count - accepted.Count).ConfigureAwait(true);
    }

    // ── 붙여넣기 (§4.6 310행 · 322~323행 · A군 ③ C-2) ────────────────────────
    //
    // 클립보드에서 온 것도 <b>셸에서 온 것</b>이다 — 포맷이 같고(CF_HDROP), 목적지가 같고
    // (타일의 currentPath), 받을지 말지의 판정도 같다. 그래서 여기서 하는 일은 "꺼내서 ·
    // 거절할 것을 거절하고 · 기존 드롭 판정에 얹는 것"뿐이다. 새 파서도 새 판정도 없다.

    /// <summary><c>DROPEFFECT_MOVE</c> — 탐색기가 <c>Ctrl+X</c> 로 실어 놓는 값.</summary>
    private const uint DropEffectMove = 2;

    /// <summary>
    /// 클립보드의 것을 <paramref name="destinationPath"/> 로 <b>복사</b>한다.
    /// </summary>
    /// <remarks>
    /// 거절은 전부 <b>스스로 지워지는</b> 상태 바 경고다(§5.1) — 사용자가 키를 눌러 생긴 거절이고
    /// 화면이 이미 답을 보여준다. 파일 시스템 실패는 그 뒤 <see cref="ReportOperation"/> 이
    /// <b>남는</b> 등급으로 적는다.
    /// <para>
    /// 목적지를 고르지도, 받을 수 있는지 새로 판정하지도 않는다 — 목적지는 패널이 넘긴 값이고
    /// 판정은 셸 드롭이 쓰는 <see cref="CanDropOnto(string?, IReadOnlyList{OperationItem})"/> ·
    /// <see cref="AcceptedForDrop"/> 그대로다.
    /// </para>
    /// </remarks>
    public async Task PasteIntoAsync(string? destinationPath)
    {
        if (destinationPath is null)
        {
            // 아직 폴더를 안 띄운 순환 칸이거나 접근 불가 폴더를 보고 있는 타일이다.
            Warn("붙여넣을 곳이 없다 — 폴더를 먼저 띄워라.", transient: true);
            return;
        }

        if (_clipboard.GetData() is not { } data)
        {
            Warn("클립보드에 붙여넣을 파일이 없다.", transient: true);
            return;
        }

        // 잘라내기를 조용히 복사로 처리하지 않는다 — 원본을 지우는 것은 이 앱이 하는 일이 아니다.
        if (WantsMove(data))
        {
            Warn("잘라내기한 것은 아직 붙여넣지 못한다 — 원본은 그대로 두었다. 복사해서 붙여넣어라.",
                transient: true);
            return;
        }

        // 셸 드롭이 쓰는 그 파서다 (§4.9) — 클립보드도 같은 포맷이라 변환 계층이 없다.
        var items = Views.DropRouter.ShellItems(data);

        if (items.Count == 0)
        {
            Warn("클립보드에 붙여넣을 파일이 없다.", transient: true);
            return;
        }

        if (!CanDropOnto(destinationPath, items))
        {
            // 드롭 판정과 <b>같은 답</b>이다. 드래그는 과녁을 안 켜는 것으로 끝나지만 키는
            // 아무 일도 안 일어난 것처럼 보이므로 이유를 적는다.
            Warn("여기에는 붙여넣을 것이 없다 — 온 곳과 같은 폴더이거나, 폴더를 자기 안에 " +
                 "넣는 것이거나, 다른 조작이 도는 중이다.", transient: true);
            return;
        }

        var accepted = AcceptedForDrop(destinationPath, items);

        await StartOperationAsync(
            FileOperationKind.Copy,
            accepted,
            [destinationPath],
            confirmMove: false,
            sameFolderSkipped: items.Count - accepted.Count,
            verbOverride: "붙여넣기").ConfigureAwait(true);
    }

    /// <summary>
    /// 클립보드가 <b>잘라내기</b>를 실었는가 — <c>Preferred DropEffect</c> 를 읽는다.
    /// </summary>
    /// <remarks>
    /// 싣는 쪽(<see cref="Views.ShellExport"/>)은 복사만 싣고 이 앱은 잘라내기를 만들지 않는다.
    /// <b>읽는 쪽이 여기 처음 생긴다</b> — 탐색기에서 <c>Ctrl+X</c> 한 것이 들어올 수 있어서다.
    /// <para>
    /// 값이 <b>없거나</b> 4바이트가 안 되거나 <c>2</c> 가 아니면 복사로 본다. 셸에 따라 복사에
    /// <c>DROPEFFECT_COPY|DROPEFFECT_LINK</c>(5) 를 싣는 것이 있어 "2일 때만 이동"으로 둔다.
    /// <b>탐색기가 실제로 무엇을 싣는지는 이 묶음에서 재지 않았다 — 손 확인 항목이다.</b>
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 밖으로 넘겼다 (§4.9 · Phase 5 Stage 2). <b>여기만 "결과"를 적지 못한다.</b>
    /// </summary>
    /// <remarks>
    /// §4.2-2 의 "결과를 상태 바에 반드시 적는다"가 적용되지 않는 <b>예외 자리</b>다. 복사를
    /// 셸이 하므로 성패를 앱이 알 수 없고, <c>Shift</c> 를 누르면 셸이 이동을 고르므로 복사인지
    /// 이동인지도 모른다. <b>"복사했다"고 적지 않는다</b> — 넘겼다는 사실까지만이 아는 전부다.
    /// <para>§5.1 의 <b>정보</b> 등급이다: 보조색, 몇 초 뒤 사라진다.</para>
    /// </remarks>
    private void ReportExported(int itemCount) =>
        Inform($"{itemCount}개를 앱 밖으로 넘겼다 — 그 뒤는 셸이 한다.");

    // ── 휴지통 정리 (§4.4 · Phase 3 Stage 3) ─────────────────────────────────

    public bool CanTrashSelection => HasSelection && !IsOperationRunning;

    /// <summary>
    /// 고른 것을 휴지통으로 (§4.4). <b>영구 삭제가 아니다</b> — 폴더면 통째로 들어간다.
    /// </summary>
    /// <remarks>
    /// <b>확인창을 두지 않는다.</b> §4.4 가 휴지통을 고른 이유가 "실수 방지"이고, 되돌릴 수
    /// 있는 동작 앞에 확인을 세우면 임시 파일을 훑어 지우는 이 경로가 매번 막힌다. 대신
    /// 몇 개를 보냈는지 반드시 알리고(요구사항), 하나라도 실패하면 요약이 상태 바에 남는다.
    /// 되돌릴 수 없는 영구 삭제는 <see cref="RejectPermanentDelete"/> 로 아예 막는다.
    /// </remarks>
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

    /// <summary><c>Shift+Del</c> — 탐색기와 달리 막는다 (§4.4).</summary>
    [RelayCommand]
    public void RejectPermanentDelete() =>
        Warn("영구 삭제는 하지 않는다. Del 로 휴지통에 보내면 되돌릴 수 있다.", transient: true);

    // ── 경로 비교 (드롭 과녁 판정) ───────────────────────────────────────────

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

    /// <summary>닿을 수 없는 경로여도 던지지 않는다 — 과녁 판정은 I/O 없이 문자열로 끝낸다.</summary>
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

        // 겹치는 것이 있는데도 여기까지 왔다면 "묻지 않기"로 해 둔 상태다 — 무엇을 할지 적는다.
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

    /// <summary>결과 보고 = 성공 / 건너뜀 / 실패 + 이유 (§5).</summary>
    /// <param name="successMessage">
    /// 전부 성공했을 때 요약 대신 쓸 문구. 휴지통처럼 "성공 3 · 건너뜀 0 · 실패 0"보다
    /// "3개를 휴지통으로 보냈다"가 답인 경우에 쓴다. 실패가 하나라도 있으면 무시된다 —
    /// 그때는 무엇이 안 됐는지가 요약이어야 하기 때문이다.
    /// </param>
    /// <param name="sameFolderSkipped">
    /// ①(온 곳과 같은 폴더)로 엔진 앞에서 빠진 건수 (§4.9 — "건너뛴 건수는 결과에 적는다").
    /// 사유가 전부 같고 요약 한 줄로 다 말할 수 있어 <b>사유 목록 창은 열지 않는다</b> —
    /// 대신 부분 결과이므로 등급은 §5.1 의 <b>남는</b> 쪽이다.
    /// </param>
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
            // 덮어쓰기는 차단하지 않되 일어났음을 반드시 알린다 (§4.3).
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

        // 성공하지 못한 것이 있으면 요약은 상태 바에 남기고, 사유는 한 줄씩 따로 보여준다.
        Warn(summary);

        // ① 만으로 빠진 것은 사유 목록 창을 열지 않는다 — 엔진이 돌려준 사유가 없고, 빠진
        // 이유는 요약에 이미 적혀 있다. 셸에서 파일 몇 개를 끌어올 때마다 창이 뜨면 확인창을
        // 두지 않는다는 §4.9 의 결과 경로가 사실상 창 있는 경로가 된다.
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
        var probes = Rows.Select(ProbeRowAsync).ToList();

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

    /// <summary>수동 갱신 <c>F5</c> + 창 활성화 시 재조회 (§8). 감시자는 타일마다 따로 붙는다.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAllAsync()
    {
        ClearMessage();

        await Task.WhenAll(Panels.Where(p => p.Entry is not null).Select(p => p.RefreshAsync()))
            .ConfigureAwait(true);
        await Task.WhenAll(Rows.Select(ProbeRowAsync)).ConfigureAwait(true);

        UpdateRowStates();
    }

    /// <summary>종료 직전 — 감시자를 풀고 밀린 디바운스 저장을 마무리한다.</summary>
    public void Shutdown()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

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

    // ── 폴더 등록·제거·수정 (§4.11) ──────────────────────────────────────────

    /// <summary>
    /// 타일 파일 목록에서 우클릭한 하위 폴더를 워크스페이스에 등록한다 (§4.11-1).
    /// </summary>
    /// <remarks>
    /// <b>넣는 자리가 둘이다</b> — 저장되는 <c>folders[]</c> 와 화면이 읽는 <see cref="Rows"/> 는
    /// 워크스페이스를 열 때 한 번 맞춰질 뿐 그 뒤로 서로를 따라가지 않는다. 둘 다에 넣고 저장한다.
    /// <para>
    /// <b>목록 맨 뒤에 붙인다.</b> 정렬을 새로 넣지 않는다 — 지금 순서는 파일에 적힌 순서이고,
    /// 등록이 그 순서를 흔들면 왼쪽 리스트가 이유 없이 뒤바뀐 것처럼 보인다.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void RegisterFolder(FileItemViewModel? item)
    {
        // 파일에는 뜻이 없다. 메뉴가 이미 감추지만 커맨드 쪽에서도 막는다 — 통로가 하나 더
        // 생기면(단축키 등) 메뉴의 판정만으로는 못 막는다.
        if (item is null || !item.IsDirectory)
        {
            return;
        }

        var normalized = FolderPathRules.Normalize(item.FullPath);

        // D3 — 같은 path 는 거절한다. 다만 거절은 여기(UI 층)에서만이다. 파일을 여는 시점의
        // 검증(WorkspaceStore)에는 넣지 않는다 — 손으로 고치다 겹친 사용자가 워크스페이스
        // 자체를 못 열게 되는 쪽이 더 나쁘다.
        if (Workspace.Folders.Any(f => PathsEqual(f.Path, normalized)))
        {
            Warn($"이미 등록된 폴더다: {normalized}", transient: true);
            return;
        }

        var entry = new FolderEntry
        {
            Path = normalized,

            // 기본 표시 이름은 잎 이름, 설명은 빈 값으로 시작한다 (§4.11-1).
            DisplayName = FolderPathRules.LeafName(normalized),
        };

        Workspace.Folders.Add(entry);
        Rows.Add(new FolderRowViewModel(entry));

        // 새 행에도 "순환 칸 지정" 선택지가 있어야 한다 — 배치는 안 바뀌었으므로 그것만 다시 센다.
        RefreshRotationChoices();
        Save();

        Inform($"등록했다: {entry.DisplayName}");
    }

    /// <summary>
    /// 왼쪽 리스트에서 고른 폴더 등록을 지운다 (§4.11-2). <b>실제 폴더는 건드리지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 폴더 하나가 화면 배치와 저장된 규칙 여러 곳에 동시에 걸려 있어서, <b>딸려 갈 것을 먼저
    /// 센다</b> — 세어 보이지 않으면 무엇이 사라지는지 누르기 전에 알 방법이 없다(D1).
    /// <para>
    /// 고아 참조 셋(타일·대상함·매크로)의 처리가 지금은 제각각이다. <b>제거 경로는 셋을 같은
    /// 규칙으로 맞춘다: 세어서 보이고, 지우고, 저장한다</b>(E3). 매크로 정의에 남은 죽은 id 도
    /// 이때 같이 지운다 — 실행할 때만 거르면 정의에는 영영 남는다.
    /// </para>
    /// <para>
    /// <b>세는 것은 상태를 바꾸지 않는다.</b> 취소하면 아무것도 바뀌지 않아야 한다.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void RemoveFolder(FolderRowViewModel? row)
    {
        if (row is null || !Rows.Contains(row))
        {
            return;
        }

        var id = row.Entry.Id;

        // ── 센다 (읽기만 한다) ────────────────────────────────────────────────

        // 타일: 상시 칸이 못박은 폴더만이 이 폴더를 "쓴다". 순환 칸은 folderId 를 갖지 않는다
        // — GridLayout.NormalizeTiles 가 폴더가 사라진 타일을 세는 기준과 같다.
        var doomedTiles = Tiles
            .Where(t => t.Spec.Kind == TileKind.Pinned && t.Spec.FolderId == id)
            .ToList();

        // 대상함: copyTray[] 는 폴더 id 를 그냥 담은 List<Guid> 다 — folderId 라는 이름이 아니다.
        var trayCount = Workspace.CopyTray?.Count(g => g == id) ?? 0;

        // 매크로: 목적지가 "고정 폴더"인 규칙이 이 id 를 들고 있는 것.
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

        // ── 지운다 ───────────────────────────────────────────────────────────

        foreach (var tile in doomedTiles)
        {
            Workspace.Tiles!.Remove(tile.Spec);
            Tiles.Remove(tile);
            tile.Panel.Clear();
            DetachWatcher(tile);
        }

        // 순환 칸이 지금 이 폴더를 띄우고 있으면 비운다 — 등록이 없어진 폴더를 계속 보여 줄 수 없다.
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

            // 비면 슬롯 자체를 지운다 — 빈 배열을 파일에 남기지 않는다(copyTray·macros 와 같은 결).
            if (macro.Dest.FolderIds.Count == 0)
            {
                macro.Dest.FolderIds = null;
            }

            Macros.FirstOrDefault(c => ReferenceEquals(c.Definition, macro))?.NotifyChanged();
        }

        Rows.Remove(row);
        Workspace.Folders.Remove(row.Entry);

        // ── 저장한다 ─────────────────────────────────────────────────────────
        //
        // SaveTray 가 copyTray[] 를 Tray 에서 되쓰고 Save 까지 부른다 — 대상함이 안 걸렸으면
        // 그 경로를 타지 않으므로 아래에서 따로 저장한다.
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

    /// <summary>
    /// 왼쪽 리스트에서 고른 폴더의 <c>displayName</c>·<c>description</c> 을 고친다 (§4.11-3).
    /// </summary>
    /// <remarks>
    /// <b>고치는 것은 둘뿐이다.</b> <c>pinned</c> 는 상시 타일의 그림자라 여기서 손대면 다음
    /// 배치 갱신이 되돌려 놓는다(D2). <c>viewMode</c>·<c>sortBy</c>·<c>sortDesc</c>·
    /// <c>rotationSlot</c> 은 각자의 기존 진입점을 그대로 쓴다.
    /// <para>
    /// <c>description</c> 은 <b>값이 들어간 적이 한 번도 없는 필드</b>다 — 여기가 그 첫 경로다.
    /// </para>
    /// </remarks>
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

        // 이름·설명은 FolderEntry 에서 계산되는 값이라 알림이 저절로 걸리지 않는다 — 같은 폴더를
        // 보여 주는 세 곳(왼쪽 리스트·대상함·타일 헤더)에 손으로 알린다.
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

    /// <summary>
    /// 우클릭한 하위 폴더를 그 타일의 앵커로 지정한다 (§4.6 305행 · A군 ① D8~D11).
    /// 반환값은 <b>앵커가 실제로 바뀌었는가</b>다.
    /// </summary>
    /// <remarks>
    /// <b><c>folderId</c> 는 그대로 두고 <c>path</c> 만 간다.</b> 새 id 를 만들면 대상함
    /// (<c>copyTray[]</c>)·매크로(<c>macros[].dest.folderIds</c>)·상시 타일
    /// (<c>tiles[].folderId</c>)이 들고 있는 참조가 전부 고아가 된다.
    /// <para>
    /// <b>어느 항목의 <c>path</c> 를 가는가</b> — 우클릭이 일어난 <b>그 패널의 앵커</b>다.
    /// 패널은 <see cref="FolderPanelViewModel.Entry"/> 로 <c>FolderEntry</c> 를 <b>객체 참조</b>로
    /// 들고 있고, <see cref="Rows"/> 와 <c>Workspace.Folders</c> 도 같은 객체를 가리킨다
    /// (<see cref="EditFolder"/> 가 쓰는 짝맞춤과 같다). 그래서 필드를 한 번 고치면 저장되는
    /// <c>folders[]</c> 쪽은 그것으로 끝이고, <b>남는 일은 알림</b>이다 — 화면 쪽 값들이 전부
    /// <c>Entry</c> 에서 계산되는데 통지가 저절로 걸리지 않는다.
    /// </para>
    /// <para>
    /// <b>거절은 여기(UI 층)에서만이다</b> — 파일을 여는 시점의 검증에는 넣지 않는다
    /// (§4.11-1 D3 과 같은 규칙).
    /// </para>
    /// </remarks>
    private bool SetAnchorFromItem(FolderPanelViewModel panel, FileItemViewModel item)
    {
        if (!item.IsDirectory || panel.Entry is not { } entry)
        {
            return false;
        }

        // 빈 순환 칸이 아닌데 등록 목록에 없는 앵커는 있을 수 없다 — 있으면 손댈 곳이 없다.
        var row = Rows.FirstOrDefault(r => ReferenceEquals(r.Entry, entry));
        if (row is null)
        {
            return false;
        }

        var normalized = FolderPathRules.Normalize(item.FullPath);

        // 앵커 위로 올라가 있으면 목록에 앵커 자신이 보인다 — 제자리 지정은 할 일이 없다.
        if (PathsEqual(entry.Path, normalized))
        {
            Warn($"이미 앵커다: {normalized}", transient: true);
            return false;
        }

        // D9 — 다른 등록이 이미 이 경로를 갖고 있으면 거절하고 이유를 말한다.
        if (Workspace.Folders.Any(f => !ReferenceEquals(f, entry) && PathsEqual(f.Path, normalized)))
        {
            Warn($"이미 등록된 폴더다: {normalized}", transient: true);
            return false;
        }

        // D10 — 표시 이름을 따라 바꿀지만 묻는다. description 은 편집 대상이 아니다.
        // 잎 이름은 D4 의 한 곳에서 뽑는다 — 여기서 문자열을 자르지 않는다.
        var leaf = FolderPathRules.LeafName(normalized);
        if (_prompt.Confirm(
                "앵커 지정",
                $"'{row.DisplayName}' 의 앵커를 다음으로 옮긴다:\n{normalized}\n\n" +
                $"표시 이름도 '{leaf}' 로 바꿀까?"))
        {
            entry.DisplayName = leaf;
        }

        entry.Path = normalized;

        // 이름·경로는 FolderEntry 에서 계산되는 값이라 알림이 저절로 걸리지 않는다 — 같은 폴더를
        // 보여 주는 세 곳(왼쪽 리스트·대상함·타일 헤더)에 손으로 알린다(EditFolder 와 같은 결).
        row.NotifyEdited();

        foreach (var trayEntry in Tray.Where(t => ReferenceEquals(t.Entry, entry)))
        {
            trayEntry.NotifyEdited();
        }

        foreach (var p in Panels.Where(p => ReferenceEquals(p.Entry, entry)))
        {
            p.NotifyAnchorName();
        }

        // 공용 저장 경로다 — 디바운스가 그대로 걸린다(§7.1). currentPath 는 저장 대상이 아니다.
        Save();

        Inform($"앵커를 옮겼다: {row.DisplayName} → {normalized}");
        return true;
    }

    /// <summary>
    /// 우클릭(또는 <c>F2</c>)한 항목의 <b>디스크 이름</b>을 바꾼다 (§4.6 309행 · A군 ② D12~D15).
    /// 반환값은 <b>새 경로</b>이고, 거절·취소·실패면 <c>null</c> 이다.
    /// </summary>
    /// <remarks>
    /// <b>이 묶음에서 처음으로 실제 파일 시스템을 바꾼다</b> — 열기·속성은 읽기였고 등록·앵커
    /// 지정은 JSON 만 고쳤다. 되돌릴 수 없으므로 <b>거절을 전부 앞에 세우고</b> <c>Move</c> 를
    /// 맨 뒤에 둔다: 이름을 판정하고, 겹치는지 보고, 그러고 나서 옮긴다.
    /// <para>
    /// <b>거절은 여기(UI 층)에서만이다</b> — 워크스페이스를 여는 시점의 검증에는 넣지 않는다
    /// (§4.11-1 D3 · §4.11 534~536행과 같은 규칙).
    /// </para>
    /// <para>
    /// <b>이름 규칙을 여기서 다시 적지 않는다</b>(D4) —
    /// <see cref="FolderPathRules.RejectLeafName"/> 한 곳이 본다.
    /// </para>
    /// </remarks>
    private string? RenameFromItem(FileItemViewModel item)
    {
        var oldPath = FolderPathRules.Normalize(item.FullPath);
        var oldLeaf = FolderPathRules.LeafName(oldPath);

        // 드라이브 루트("D:\")는 부모가 없다 — 이름을 붙일 자리가 없다.
        if (System.IO.Path.GetDirectoryName(oldPath) is not { Length: > 0 } parent)
        {
            Warn($"이름을 바꿀 수 없다 — 드라이브 루트다: {oldPath}", transient: true);
            return null;
        }

        var typed = _prompt.AskName(
            "이름 바꾸기",
            $"'{oldLeaf}' 의 새 이름을 적어라. {(item.IsDirectory ? "폴더" : "파일")}가 실제로 바뀐다.",
            oldLeaf);

        // 취소 — 아무 일도 없었다. 상태 바에도 적지 않는다(사용자가 스스로 그만둔 것이다).
        if (typed is null)
        {
            return null;
        }

        // 끝 공백은 Windows 가 말없이 깎으므로 먼저 깎아 판정에 올린다 — 공백만 적은 것은
        // 그 결과 "빈 이름"으로 거절된다.
        var leaf = typed.Trim();

        if (FolderPathRules.RejectLeafName(leaf) is { } reason)
        {
            Warn($"이름을 바꾸지 못했다 — {reason}", transient: true);
            return null;
        }

        // 제자리 이름은 할 일이 없다. 대소문자만 바꾸는 것(a → A)은 여기를 지나 Move 로 간다 —
        // 같은 파일이므로 아래 "이미 있다" 검사도 PathsEqual 로 비켜 준다.
        if (string.Equals(leaf, oldLeaf, StringComparison.Ordinal))
        {
            return null;
        }

        var newPath = System.IO.Path.Combine(parent, leaf);

        if (!PathsEqual(oldPath, newPath)
            && (System.IO.Directory.Exists(newPath) || System.IO.File.Exists(newPath)))
        {
            Warn($"이름을 바꾸지 못했다 — 같은 이름이 이미 있다: {leaf}", transient: true);
            return null;
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
            // 삼키지 않는다. 실패는 <b>남는</b> 경고다 — 화면이 스스로 답을 보여 주지 않는다.
            Warn($"이름을 바꾸지 못했다: {oldLeaf} — {ex.Message}");
            return null;
        }

        UpdateAnchorAfterRename(oldPath, newPath, leaf, oldLeaf);
        return newPath;
    }

    /// <summary>
    /// 이름 바꾼 것이 <b>어느 등록의 앵커 자신</b>이면 <c>folders[]</c> 의 <c>path</c> 를 따라
    /// 옮긴다 (§4.6 309행 · A군 ② D12~D15).
    /// </summary>
    /// <remarks>
    /// <b>안 고치면 등록이 그 자리에서 고아가 된다</b>(D12) — 방금 사람이 바꾼 이름 때문에
    /// 앵커가 없는 경로를 가리키게 되므로, 이것은 선택이 아니라 이름 바꾸기의 일부다.
    /// <para>
    /// <b>어느 등록인지 판정하는 방법</b>: 바꾸기 <b>전</b> 경로를 <c>Rows</c> 의 앵커 경로와
    /// <see cref="PathsEqual"/>(끝 구분자·대소문자 무시)로 맞춘다. 우클릭한 타일이 무엇인지는
    /// 보지 않는다 — 다른 타일의 앵커를 옆에서 바꿔도 그 등록이 따라가야 하기 때문이다.
    /// <c>Rows[].Entry</c> 와 <c>Workspace.Folders[]</c> 는 <b>같은 객체</b>라(§5-7 ·
    /// <see cref="EditFolder"/>·<see cref="SetAnchorFromItem"/> 이 쓰는 짝맞춤과 같다) 필드를
    /// 한 번 고치면 저장되는 쪽은 그것으로 끝이고 <b>남는 일은 알림</b>이다.
    /// </para>
    /// <para>
    /// <b>D14 — 확인창의 「취소」는 <c>displayName</c> 갱신만 거절한다.</b> 이름 바꾸기 자체와
    /// <c>path</c> 갱신은 어느 단추를 눌러도 이미 일어났다. 그래서 <c>path</c> 를 <b>묻기 전에</b>
    /// 넣는다 — 물음의 답이 무엇이든 바뀌지 않는 값을 답 뒤로 미룰 이유가 없다.
    /// </para>
    /// <para>
    /// <b>D15 — 앵커가 아니면 <c>folders[]</c> 를 건드리지 않고 확인창도 안 띄운다.</b>
    /// 파일이거나, 폴더라도 어느 등록의 앵커가 아닌 흔한 경우가 전부 여기다.
    /// </para>
    /// </remarks>
    private void UpdateAnchorAfterRename(string oldPath, string newPath, string leaf, string oldLeaf)
    {
        // 파일은 앵커가 될 수 없다 — 경로 비교조차 하지 않는다.
        var row = Rows.FirstOrDefault(r => PathsEqual(r.Entry.Path, oldPath));

        if (row is null)
        {
            // D15 — 대부분이 여기다.
            Inform($"이름을 바꿨다: {oldLeaf} → {leaf}");
            return;
        }

        // 확인창 본문에 쓸 이름은 <b>고치기 전</b>에 잡는다 — DisplayName 이 비어 있으면
        // Path 의 잎으로 떨어지므로, path 를 먼저 넣으면 본문이 새 이름을 옛 이름인 양 말한다.
        var label = row.DisplayName;

        row.Entry.Path = newPath;

        // D13 — displayName 갱신 여부는 묻는다. ①이 만든 D10 경로를 그대로 재사용한다
        // (새 대화상자를 만들지 않는다). 단추 글자가 "확인 / 취소"인 것은 IUserPrompt.Confirm
        // 이 OKCancel 이기 때문이고, 예/아니오로 바꾸면 「폴더 제거」창까지 같이 바뀐다.
        if (_prompt.Confirm(
                "이름 바꾸기",
                $"'{label}' 등록의 앵커 폴더 이름이 바뀌었다:\n{newPath}\n\n" +
                $"표시 이름도 '{leaf}' 로 바꿀까?"))
        {
            row.Entry.DisplayName = leaf;
        }

        // 이름·경로는 FolderEntry 에서 계산되는 값이라 알림이 저절로 걸리지 않는다 — 같은 폴더를
        // 보여 주는 세 곳(왼쪽 리스트·대상함·타일 헤더)에 손으로 알린다(SetAnchorFromItem 과 같은 결).
        row.NotifyEdited();

        foreach (var trayEntry in Tray.Where(t => ReferenceEquals(t.Entry, row.Entry)))
        {
            trayEntry.NotifyEdited();
        }

        foreach (var panel in Panels.Where(p => ReferenceEquals(p.Entry, row.Entry)))
        {
            panel.NotifyAnchorName();
        }

        // 공용 저장 경로다 — 디바운스가 그대로 걸린다(§7.1). currentPath 는 저장 대상이 아니다.
        Save();

        Inform($"이름을 바꿨다: {oldLeaf} → {leaf} (등록 '{row.DisplayName}' 의 앵커도 따라갔다)");
    }

    /// <summary>등록 경로 비교 — 끝 구분자와 대소문자를 무시한다 (§4.11-1 D3).</summary>
    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            FolderPathRules.Normalize(a), FolderPathRules.Normalize(b), StringComparison.OrdinalIgnoreCase);

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private TileViewModel CreateTile(TileSpec spec)
    {
        var panel = new FolderPanelViewModel(
            _enumerator, _shell, _clipboard, spec.Kind == TileKind.Rotating, ReportError, ReportRejection)
        {
            // 우클릭 "휴지통으로 삭제"가 닿을 통로. 패널은 엔진을 모른다 (§4.4).
            TrashSelectionCommand = TrashSelectionCommand,

            // 셸 드롭을 받는 통로도 같은 이유로 여기서 걸어 준다 (§4.9).
            ShellDropHost = this,

            // 밖으로 넘겼다고 알리는 통로 (§4.9 · Stage 2).
            ReportExported = ReportExported,

            // 헤더에서 뷰 방식을 뒤집으면 folders[].viewMode 가 바뀐다 — pinned·배치가 이미
            // 쓰는 디바운스 저장에 얹는다(§7.1). 패널은 저장소를 모른다.
            SaveFolderEntry = Save,

            // 항목 우클릭 "새 폴더로 등록"이 닿을 통로 (§4.11-1). 패널은 Workspace 를 모른다.
            RegisterFolderCommand = RegisterFolderCommand,

            // 항목 우클릭 "이 폴더를 앵커로 지정"이 닿을 통로 (§4.6 · D8). 커맨드가 아니라
            // 델리게이트다 — 어느 타일의 앵커인지는 부르는 패널만 안다.
            SetAnchorFolder = SetAnchorFromItem,

            // 항목 우클릭 「이름 바꾸기」와 F2 가 닿을 통로 (§4.6 309행 · D12~D15). 확인창과
            // folders[] 를 아는 쪽이 여기다 — 패널은 둘 다 모른다.
            RenameOnDisk = RenameFromItem,

            // 붙여넣기(Ctrl+V · breadcrumb 우클릭)가 닿을 통로 (§4.6 322~323행 · A군 ③ C-2).
            // 클립보드를 읽고 판정하고 엔진에 얹는 일은 전부 이쪽이다 — 패널은 목적지만 안다.
            PasteInto = PasteIntoAsync,
        };

        if (spec.Kind == TileKind.Pinned && spec.FolderId is { } id)
        {
            panel.Entry = Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry;
        }

        var tile = new TileViewModel(spec, panel) { IsEditing = IsEditingLayout };
        AttachWatcher(tile);
        return tile;
    }

    /// <summary>
    /// 이 타일이 보는 폴더를 감시한다 (§8 · Phase 5). 감시자는 <see cref="IFolderEnumerator"/>
    /// 에 얹지 않는다 — 그것은 "어떻게 읽는가"의 seam 이고 이것은 "언제 읽는가"다.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Application"/> 이 안 떠 있으면 안 매단다.</b> 시험은 창도 Dispatcher 도
    /// 없이 뷰모델만 세우므로 마샬할 UI 스레드가 없다 — <see cref="IUserPrompt"/> 가 소유 창을
    /// 찾을 때 쓰는 판정과 같은 결이다.
    /// </remarks>
    private void AttachWatcher(TileViewModel tile)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        // 조작 중 억제는 <see cref="IsOperationRunning"/> 을 그대로 읽는다 — 같은 뜻의
        // 플래그를 새로 만들면 진실이 두 곳이 되어 하나만 켜지는 사고가 난다.
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

    /// <summary>거절 — 화면이 이미 답을 보여주므로 스스로 사라진다 (§5.1).</summary>
    private void ReportRejection(string text) => Warn(text, transient: true);
}
