using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

public sealed class WorkspaceRoundTripTests
{
    [Fact]
    public void SaveThenLoadPreservesBasicFields()
    {
        using var temp = new TempStore();
        var original = new Workspace
        {
            Title = "라운드트립",
            Description = "설명 있음",
            LastUsed = new DateTimeOffset(2026, 3, 1, 13, 45, 30, TimeSpan.FromHours(9)),
            Folders =
            [
                new FolderEntry
                {
                    Path = @"D:\some\code",
                    DisplayName = "code",
                    Description = "구현",
                    ViewMode = FolderViewMode.Details,
                    SortBy = SortBy.Size,
                    SortDesc = true,
                    Pinned = true,
                },
                new FolderEntry
                {
                    Path = @"D:\some\logs",
                    ViewMode = FolderViewMode.ExtraLargeIcons,
                    SortBy = SortBy.Type,
                },
            ],
        };

        Assert.Equal(0, original.SchemaVersion);
        Assert.Null(temp.Store.SaveWorkspace(original));
        Assert.Equal(Workspace.CurrentSchemaVersion, original.SchemaVersion);

        var reloaded = temp.Store.LoadWorkspace(original.Id).ValueOrThrow();

        Assert.Equal(Workspace.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.Equal(original.Id, reloaded.Id);
        Assert.Equal(original.Title, reloaded.Title);
        Assert.Equal(original.Description, reloaded.Description);
        Assert.Equal(original.LastUsed, reloaded.LastUsed);
        Assert.Equal(2, reloaded.Folders.Count);

        Assert.Equal(original.Folders[0].Id, reloaded.Folders[0].Id);
        Assert.Equal(original.Folders[0].Path, reloaded.Folders[0].Path);
        Assert.Equal(SortBy.Size, reloaded.Folders[0].SortBy);
        Assert.True(reloaded.Folders[0].SortDesc);
        Assert.True(reloaded.Folders[0].Pinned);

        Assert.Null(reloaded.Folders[1].DisplayName);
        Assert.Equal(FolderViewMode.ExtraLargeIcons, reloaded.Folders[1].ViewMode);
        Assert.Equal(SortBy.Type, reloaded.Folders[1].SortBy);
    }

    [Fact]
    public void NullOptionalSlotsAreOmittedFromTheFile()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "슬롯 없음" };

        temp.Store.SaveWorkspace(workspace);
        var json = File.ReadAllText(temp.Paths.WorkspaceFile(workspace.Id));

        Assert.DoesNotContain("\"grid\"", json);
        Assert.DoesNotContain("\"tiles\"", json);
        Assert.DoesNotContain("\"window\"", json);
        Assert.DoesNotContain("\"copyTray\"", json);
        Assert.DoesNotContain("\"macros\"", json);
        Assert.DoesNotContain("\"description\"", json);

        Assert.Contains("\n  \"id\":", json.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void EnumsAreWrittenAsCamelCaseStrings()
    {
        using var temp = new TempStore();
        var workspace = new Workspace
        {
            Title = "enum",
            Folders = [new FolderEntry { Path = @"D:\x", ViewMode = FolderViewMode.ExtraLargeIcons, SortBy = SortBy.Modified }],
        };

        temp.Store.SaveWorkspace(workspace);
        var json = File.ReadAllText(temp.Paths.WorkspaceFile(workspace.Id));

        Assert.Contains("\"viewMode\": \"extraLargeIcons\"", json);
        Assert.Contains("\"sortBy\": \"modified\"", json);
    }

    [Fact]
    public void LayoutSlotsSurviveARoundTrip()
    {
        using var temp = new TempStore();
        var folderId = Guid.NewGuid();
        var workspace = new Workspace
        {
            Title = "슬롯 보존",
            Folders = [new FolderEntry { Id = folderId, Path = @"D:\x" }],
            Grid = new GridSpec { Cols = 4, Rows = 4 },
            Tiles =
            [
                new TileSpec { FolderId = folderId, CellX = 1, CellY = 2, SpanX = 2, SpanY = 3, Kind = TileKind.Rotating },
            ],
            Window = new WindowSpec { X = 10, Y = 20, Width = 1400, Height = 900, MonitorId = "\\\\.\\DISPLAY1" },
            CopyTray = [folderId],
            Macros =
            [
                new MacroDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "산출물 배포",
                    Op = FileOperationKind.Copy,
                    Source = new MacroSource { Kind = MacroSourceKind.FixedPath, Path = @"D:\out\a.dll" },
                    Dest = new MacroDest { Kind = MacroDestKind.FolderIds, FolderIds = [folderId] },
                    OnConflict = ConflictPolicy.Overwrite,
                    Confirm = true,
                },
            ],
        };

        temp.Store.SaveWorkspace(workspace);
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.Equal(4, reloaded.Grid!.Cols);
        var tile = Assert.Single(reloaded.Tiles!);
        Assert.Equal(TileKind.Rotating, tile.Kind);
        Assert.Equal(2, tile.SpanX);
        Assert.Equal(1400, reloaded.Window!.Width);
        Assert.Equal("\\\\.\\DISPLAY1", reloaded.Window.MonitorId);
        Assert.Equal(folderId, Assert.Single(reloaded.CopyTray!));

        var macro = Assert.Single(reloaded.Macros!);
        Assert.Equal(FileOperationKind.Copy, macro.Op);
        Assert.Equal(MacroSourceKind.FixedPath, macro.Source!.Kind);
        Assert.Equal(MacroDestKind.FolderIds, macro.Dest!.Kind);
        Assert.Equal(ConflictPolicy.Overwrite, macro.OnConflict);
        Assert.True(macro.Confirm);
    }

    [Fact]
    public void ShowSizeAndShowModifiedSurviveARoundTrip()
    {
        using var temp = new TempStore();
        var workspace = new Workspace
        {
            Title = "컬럼 표시 왕복",
            Folders =
            [
                new FolderEntry { Path = @"D:\some\code", ShowSize = false, ShowModified = true },
                new FolderEntry { Path = @"D:\some\logs", ShowSize = true, ShowModified = false },
            ],
        };

        temp.Store.SaveWorkspace(workspace);
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.False(reloaded.Folders[0].ShowSize);
        Assert.True(reloaded.Folders[0].ShowModified);
        Assert.True(reloaded.Folders[1].ShowSize);
        Assert.False(reloaded.Folders[1].ShowModified);
    }

    [Fact]
    public void ShowPreviewAndPreviewRatioSurviveARoundTrip()
    {
        using var temp = new TempStore();
        var workspace = new Workspace
        {
            Title = "미리보기 왕복",
            Folders =
            [
                new FolderEntry { Path = @"D:\some\code", ShowPreview = true, PreviewRatio = 0.6 },
                new FolderEntry { Path = @"D:\some\logs", ShowPreview = false, PreviewRatio = 0.2 },
            ],
        };

        temp.Store.SaveWorkspace(workspace);
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.True(reloaded.Folders[0].ShowPreview);
        Assert.Equal(0.6, reloaded.Folders[0].PreviewRatio);
        Assert.False(reloaded.Folders[1].ShowPreview);
        Assert.Equal(0.2, reloaded.Folders[1].PreviewRatio);
    }

    [Fact]
    public void ASearchTileSurvivesARoundTrip()
    {
        using var temp = new TempStore();
        var workspace = new Workspace
        {
            Title = "검색 타일 왕복",
            Tiles = [new TileSpec { Kind = TileKind.Search, CellX = 2, CellY = 1, SpanX = 1, SpanY = 1 }],
        };

        temp.Store.SaveWorkspace(workspace);
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();

        var tile = Assert.Single(reloaded.Tiles!);
        Assert.Equal(TileKind.Search, tile.Kind);
        Assert.Null(tile.FolderId);
        Assert.Equal(2, tile.CellX);
        Assert.Equal(1, tile.CellY);
    }
}
