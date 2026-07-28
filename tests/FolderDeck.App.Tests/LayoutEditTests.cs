using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>배치 편집 모드 · 타일 이동/리사이즈 · 상시↔순환 · 다개 순환 (§3.2 · Phase 2).</summary>
public sealed class LayoutEditTests
{
    private static MainWindowFixture Editing()
    {
        var f = new MainWindowFixture();
        f.ViewModel.ToggleLayoutEdit();
        return f;
    }

    private static GridCellViewModel FreeCell(MainWindowFixture f) =>
        f.ViewModel.Cells.First(c => c.IsFree);

    // ── 모드 전환 ────────────────────────────────────────────────────────────

    [Fact]
    public void ViewModeIsTheDefaultAndTilesAreLocked()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.IsEditingLayout);
        Assert.True(f.ViewModel.IsViewMode);
        Assert.True(tile.IsViewMode);

        // 뷰 모드에서는 움직이지 않는다.
        Assert.False(f.ViewModel.MoveTile(tile, 2, 2));
        Assert.False(f.ViewModel.ResizeTile(tile, 1, 1));
        Assert.Equal((0, 0, 2, 2), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ToggleFlipsEveryTileIntoEditMode()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleLayoutEdit();

        Assert.True(f.ViewModel.IsEditingLayout);
        Assert.All(f.ViewModel.Tiles, t => Assert.True(t.IsEditing));
        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsViewMode));

        f.ViewModel.ToggleLayoutEdit();

        Assert.False(f.ViewModel.IsEditingLayout);
        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsEditing));
    }

    /// <summary>편집 중에는 왼쪽 리스트 클릭이 순환 칸을 갈아치우지 않는다.</summary>
    [Fact]
    public async Task RowClicksAreIgnoredWhileEditing()
    {
        using var f = Editing();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.True(f.Rotating.IsEmpty);
    }

    // ── 이동 · 리사이즈 ──────────────────────────────────────────────────────

    [Fact]
    public void MovingATileSnapsToTheCellAndPersists()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        Assert.True(f.ViewModel.MoveTile(tile, 2, 2));

        Assert.Equal((2, 2), (tile.CellX, tile.CellY));
        Assert.Equal((2, 2), (tile.Spec.CellX, tile.Spec.CellY));

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal((2, 2), (reloaded.Tiles![0].CellX, reloaded.Tiles[0].CellY));
    }

    [Fact]
    public void MovingOutsideTheGridIsRejected()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];   // 2x2

        Assert.False(f.ViewModel.MoveTile(tile, 3, 0));
        Assert.False(f.ViewModel.MoveTile(tile, 0, 3));
        Assert.Equal((0, 0), (tile.CellX, tile.CellY));
    }

    [Fact]
    public void MovingOntoAnotherTileIsRejectedNotPushedAside()
    {
        using var f = Editing();
        var first = f.ViewModel.Tiles[0];    // (0,0) 2x2
        var second = f.ViewModel.Tiles[1];   // (2,0) 2x2

        Assert.False(f.ViewModel.MoveTile(first, 2, 0));

        Assert.Equal((0, 0), (first.CellX, first.CellY));
        Assert.Equal((2, 0), (second.CellX, second.CellY));
    }

    [Fact]
    public void MovingIntoTheEmptyQuadrantWorks()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        // (2,2) 는 유도 배치에서 비어 있는 사분면이다.
        Assert.True(f.ViewModel.MoveTile(tile, 2, 2));
        Assert.Equal(4, f.ViewModel.Cells.Count(c => c.IsFree));
        Assert.True(f.ViewModel.Cells.Single(c => c is { X: 0, Y: 0 }).IsFree);
    }

    [Fact]
    public void ResizingStaysInsideTheGrid()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[2];   // 순환 칸, (0,2) 2x2

        Assert.False(f.ViewModel.ResizeTile(tile, 2, 3));   // 아래로 삐져나감
        Assert.True(f.ViewModel.ResizeTile(tile, 4, 2));    // 가로 폭 전체는 빈 자리라 가능
        Assert.Equal((4, 2), (tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ResizingIntoAnotherTileIsRejected()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];   // (0,0) 2x2 — 오른쪽에 (2,0) 타일이 있다

        Assert.False(f.ViewModel.ResizeTile(tile, 3, 2));
        Assert.Equal((2, 2), (tile.SpanX, tile.SpanY));

        // 아래는 (0,2) 타일이 막고 있고, 1x1 로 줄이는 건 언제나 된다.
        Assert.True(f.ViewModel.ResizeTile(tile, 1, 1));
        Assert.Equal((1, 1), (tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ResizeAndMovePersistAcrossARestart()
    {
        using var f = Editing();
        f.ViewModel.ResizeTile(f.ViewModel.Tiles[0], 1, 1);
        f.ViewModel.MoveTile(f.ViewModel.Tiles[0], 3, 3);
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard);

        var tile = restarted.Tiles[0];
        Assert.Equal((3, 3, 1, 1), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
    }

    // ── 타일 제거 ────────────────────────────────────────────────────────────

    [Fact]
    public void RemovingATileKeepsTheFolderRegistered()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];
        var name = f.Row("code").DisplayName;

        f.ViewModel.RemoveTile(tile);

        Assert.Equal(2, f.ViewModel.Tiles.Count);
        Assert.Equal(2, f.Workspace.Tiles!.Count);

        // 폴더 등록은 folders[] 에 남고 왼쪽 리스트에도 그대로 있다.
        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Contains(f.ViewModel.Rows, r => r.DisplayName == name);

        // 비워진 사분면이 빈 칸으로 잡힌다.
        Assert.Equal(8, f.ViewModel.Cells.Count(c => c.IsFree));
    }

    // ── 상시 ↔ 순환 ─────────────────────────────────────────────────────────

    [Fact]
    public void PinnedTileBecomesRotatingWithTheLowestFreeNumber()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsRotating);
        Assert.Equal(2, tile.RotationIndex);    // 순환1 은 이미 있다
        Assert.Equal("순환2", tile.KindTag);
        Assert.Null(tile.Spec.FolderId);
        Assert.True(tile.Panel.IsEmpty);
        Assert.True(tile.Panel.IsRotating);
    }

    [Fact]
    public async Task RotatingTileBecomesPinnedOnWhateverItIsShowing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        f.ViewModel.ToggleLayoutEdit();
        var tile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsPinned);
        Assert.Null(tile.RotationIndex);
        Assert.Equal(f.Row("문서").Entry.Id, tile.Spec.FolderId);
        Assert.Equal(f.DocsPath, tile.Panel.Entry!.Path);
    }

    /// <summary>빈 순환 칸은 못박을 대상이 없다 — 조용히 실패하지 않고 이유를 말한다.</summary>
    [Fact]
    public void EmptyRotatingTileCannotBePinned()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles.Single(t => t.IsRotating);

        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsRotating);
        Assert.Contains("비어 있어", f.ViewModel.Message);
    }

    // ── 다개 순환 + 번호 태그 ────────────────────────────────────────────────

    [Fact]
    public void SecondRotatingTileGetsNumberTwoAndRowsShowTheirTag()
    {
        using var f = Editing();

        Assert.All(f.ViewModel.Rows, r => Assert.False(r.ShowRotationTag));

        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));

        var tags = f.ViewModel.Tiles.Where(t => t.IsRotating).Select(t => t.KindTag);
        Assert.Equal(["순환1", "순환2"], tags);

        // 순환 칸이 둘 이상이면 순환 전용 행에 번호가 붙는다(하나뿐일 땐 소음이라 숨긴다).
        Assert.True(f.Row("문서").ShowRotationTag);
        Assert.True(f.Row("작업").ShowRotationTag);
        Assert.Equal("순환1", f.Row("문서").RotationSlotText);
        Assert.Equal([1, 2], f.Row("문서").RotationChoices.Select(c => c.Slot));

        // 상시 칸에 못박힌 폴더는 클릭해도 순환 칸으로 안 가므로 번호를 달지 않는다.
        Assert.False(f.Row("code").ShowRotationTag);
        Assert.False(f.Row("산출물").ShowRotationTag);
    }

    /// <summary>상시 타일을 빼면 그 폴더도 다시 순환 칸을 쓰게 되므로 번호가 돌아온다.</summary>
    [Fact]
    public void RemovingAPinnedTileGivesThatFolderItsRotationTagBack()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        Assert.False(f.Row("code").ShowRotationTag);

        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == f.Row("code").Entry.Id));

        Assert.True(f.Row("code").ShowRotationTag);
    }

    [Fact]
    public async Task AFolderAssignedToSlotTwoOpensInTheSlotTwoTile()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);
        var first = f.ViewModel.Tiles.Single(t => t.RotationIndex == 1);

        // 문서 → 순환2, 임시(설비 로그) → 순환1 기본.
        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        f.ViewModel.ToggleLayoutEdit();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, second.Panel.CurrentPath);
        Assert.True(first.Panel.IsEmpty);

        // 순환1 지정 폴더는 순환1 칸으로 — 서로 섞이지 않는다.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.Equal(f.WorkPath, first.Panel.CurrentPath);
        Assert.Equal(f.DocsPath, second.Panel.CurrentPath);
    }

    [Fact]
    public void SlotOneIsStoredAsNullSoTheFileStaysQuiet()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));

        var row = f.Row("문서");
        row.RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        Assert.Equal(2, row.Entry.RotationSlot);

        row.RotationChoices.Single(c => c.Slot == 1).SelectCommand.Execute(null);
        Assert.Null(row.Entry.RotationSlot);
        Assert.Equal(1, row.RotationSlot);
    }

    /// <summary>지정한 번호의 칸이 없으면 순환1 로 폴백하고, 어디로 갔는지 말해 준다.</summary>
    [Fact]
    public async Task AMissingRotationSlotFallsBackToSlotOne()
    {
        using var f = new MainWindowFixture();
        f.Row("문서").Entry.RotationSlot = 2;   // 순환2 칸은 배치에 없다
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
        Assert.Contains("순환2 칸 없음", f.ViewModel.Message);
    }

    [Fact]
    public void RemovingARotatingTileDemotesItsFoldersToSlotOne()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);

        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        f.Row("설비 로그").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        Assert.Equal(2, f.Row("문서").RotationSlot);

        f.ViewModel.RemoveTile(second);

        // 미아를 만들지 않는다 — 지운 번호를 쓰던 폴더는 순환1 로 내려간다.
        Assert.Equal(1, f.Row("문서").RotationSlot);
        Assert.Null(f.Row("문서").Entry.RotationSlot);
        Assert.Equal(1, f.Row("설비 로그").RotationSlot);
        Assert.Contains("순환1로", f.ViewModel.Message);
    }

    [Fact]
    public void TurningARotatingTileIntoPinnedAlsoDemotesItsFolders()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);
        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);

        // 순환2 칸에 폴더를 하나 띄워 둔 뒤 상시로 못박는다.
        second.Panel.Entry = f.Row("산출물").Entry;
        f.ViewModel.ToggleTileKind(second);

        Assert.True(second.IsPinned);
        Assert.Equal(1, f.Row("문서").RotationSlot);
    }

    /// <summary>순환 칸을 전부 지우면 클릭이 갈 곳이 없다 — 조용히 무시하지 않고 알린다.</summary>
    [Fact]
    public async Task WithNoRotatingTileLeftTheClickSaysWhy()
    {
        using var f = Editing();
        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.IsRotating));
        f.ViewModel.ToggleLayoutEdit();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Null(f.ViewModel.RotatingPanel);
        Assert.Contains("순환 칸이 없다", f.ViewModel.Message);
    }

    // ── 빈 칸에 폴더 넣기 ────────────────────────────────────────────────────

    [Fact]
    public async Task DroppingAFolderOnAFreeCellMakesAOneByOnePinnedTile()
    {
        using var f = Editing();
        var cell = FreeCell(f);

        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), cell));

        var tile = f.ViewModel.Tiles[^1];
        Assert.True(tile.IsPinned);
        Assert.Equal((cell.X, cell.Y, 1, 1), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
        Assert.Equal(f.DocsPath, tile.Panel.Entry!.Path);
        Assert.False(cell.IsFree);

        // 넣자마자 내용이 보여야 한다 — 앱을 껐다 켜야 나타나면 고장난 것이다.
        Assert.Equal(f.DocsPath, tile.Panel.CurrentPath);
        Assert.Equal("spec.md", Assert.Single(tile.Panel.Items).Name);
    }

    [Fact]
    public async Task ClickingAFreeCellAndPickingAFolderDoesTheSameThing()
    {
        using var f = Editing();
        var cell = FreeCell(f);

        f.ViewModel.BeginPlaceInCell(cell);
        Assert.True(f.ViewModel.IsFolderPickerOpen);
        Assert.Same(cell, f.ViewModel.PendingCell);

        await f.ViewModel.PlaceFolderAsync(f.Row("문서"));

        Assert.False(f.ViewModel.IsFolderPickerOpen);
        Assert.Null(f.ViewModel.PendingCell);
        Assert.Equal(f.DocsPath, f.ViewModel.Tiles[^1].Panel.CurrentPath);
    }

    /// <summary>뺐다 다시 넣어도 빈 칸으로 남지 않는다(재시작해야 보이면 고장난 것이다).</summary>
    [Fact]
    public async Task PuttingARemovedFolderBackShowsItsContentsRightAway()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.ToggleLayoutEdit();

        var code = f.ViewModel.Tiles[0];
        Assert.Equal(f.CodePath, code.Panel.CurrentPath);

        f.ViewModel.RemoveTile(code);
        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(f.Row("code"), FreeCell(f)));

        var again = f.ViewModel.Tiles[^1];
        Assert.Equal(f.CodePath, again.Panel.Entry!.Path);
        Assert.Equal(f.CodePath, again.Panel.CurrentPath);
        Assert.Equal(["Recipe", "Views", "Main.cs"], again.Panel.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task OccupiedCellsAndViewModeRejectPlacement()
    {
        using var f = new MainWindowFixture();
        var free = f.ViewModel.Cells.First(c => c.IsFree);

        // 뷰 모드에서는 아무것도 안 들어간다.
        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), free));

        f.ViewModel.ToggleLayoutEdit();
        var taken = f.ViewModel.Cells.First(c => !c.IsFree);
        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), taken));
        Assert.Equal(3, f.ViewModel.Tiles.Count);
    }

    [Fact]
    public async Task PlacedTilesSurviveARestart()
    {
        using var f = Editing();
        await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), FreeCell(f));
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard);

        Assert.Equal(5, restarted.Tiles.Count);
        Assert.Equal(2, restarted.Tiles.Count(t => t.IsRotating));
        Assert.Equal([1, 2], restarted.Tiles.Where(t => t.IsRotating).Select(t => t.RotationIndex));
    }

    /// <summary>배치는 저장되지만 하위 경로는 여전히 런타임 전용이다(§4.6).</summary>
    [Fact]
    public async Task LayoutPersistsButCurrentPathStillResetsToTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.OpenAsync(f.Rotating.Items.Single(i => i.Name == "Recipe"));

        f.ViewModel.ToggleLayoutEdit();
        f.ViewModel.MoveTile(f.ViewModel.Tiles[0], 2, 2);
        f.Store.Flush();

        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));
        Assert.DoesNotContain("Recipe", json);
        Assert.DoesNotContain("currentPath", json);

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard);
        await restarted.InitializeAsync();

        Assert.Equal((2, 2), (restarted.Tiles[0].CellX, restarted.Tiles[0].CellY));
        Assert.True(restarted.RotatingPanel!.IsEmpty);
        Assert.Equal(f.CodePath, restarted.Tiles[0].Panel.CurrentPath);
    }

    // ── 손상된 배치 (§5 — 조용히 삼키지 않는다) ──────────────────────────────

    [Fact]
    public void ATileWhoseFolderVanishedIsDroppedAndReported()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Tiles =
        [
            new TileSpec { Kind = TileKind.Pinned, FolderId = Guid.NewGuid(), CellX = 0, CellY = 0 },
            new TileSpec { Kind = TileKind.Rotating, CellX = 1, CellY = 0, RotationIndex = 1 },
        ];

        var vm = new MainViewModel(f.Workspace, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard);

        Assert.Single(vm.Tiles);
        Assert.Contains("놓을 수 없는 타일 1개", vm.Message);
    }

    // ── 상태 바 알림의 결 (Stage 0) ──────────────────────────────────────────

    /// <summary>정보성 안내는 스스로 사라진다 — 경고는 남는다.</summary>
    [Fact]
    public async Task InfoMessagesExpireButWarningsStay()
    {
        using var f = Editing();

        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        Assert.True(f.ViewModel.MessageIsTransient);
        Assert.False(f.ViewModel.MessageIsWarning);

        f.ViewModel.ExpireTransientMessage();
        Assert.False(f.ViewModel.HasMessage);

        // 클릭이 아무 데도 못 가는 상태 = 경고. 타이머가 지나가도 남는다.
        foreach (var rotating in f.ViewModel.Tiles.Where(t => t.IsRotating).ToList())
        {
            f.ViewModel.RemoveTile(rotating);
        }

        f.ViewModel.ToggleLayoutEdit();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);

        f.ViewModel.ExpireTransientMessage();
        Assert.Contains("순환 칸이 없다", f.ViewModel.Message);
    }

    /// <summary>거절은 빨강이되 스스로 지워진다 — 화면이 이미 "안 움직였다"고 답하고 있다.</summary>
    [Fact]
    public void ARefusalIsRedButDoesNotLinger()
    {
        using var f = Editing();

        f.ViewModel.ReportTileOverlap();

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    // ── 같은 폴더를 상시 칸 둘에 두지 않는다 (Stage 0) ───────────────────────

    [Fact]
    public async Task AFolderThatAlreadyHasAPinnedTileCannotBePlacedAgain()
    {
        using var f = Editing();
        var row = f.Row("code");

        Assert.False(row.CanPlaceInGrid);
        Assert.True(row.HasPinnedTile);

        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(row, FreeCell(f)));

        Assert.Equal(3, f.ViewModel.Tiles.Count);
        Assert.Contains("이미 상시 칸에 있다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    /// <summary>타일을 빼면 다시 넣을 수 있다 — 규칙은 "두 번"을 막는 것이지 폴더를 막는 게 아니다.</summary>
    [Fact]
    public async Task RemovingThePinnedTileMakesTheFolderPlaceableAgain()
    {
        using var f = Editing();
        var row = f.Row("code");

        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == row.Entry.Id));
        Assert.True(row.CanPlaceInGrid);

        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(row, FreeCell(f)));
        Assert.False(row.CanPlaceInGrid);
    }

    /// <summary>순환 칸은 폴더를 못박지 않으므로 이 규칙과 무관하다.</summary>
    [Fact]
    public void RotatingCellsAreUnaffectedByTheDuplicateRule()
    {
        using var f = Editing();

        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));
        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));

        Assert.Equal(3, f.ViewModel.Tiles.Count(t => t.IsRotating));
    }
}
