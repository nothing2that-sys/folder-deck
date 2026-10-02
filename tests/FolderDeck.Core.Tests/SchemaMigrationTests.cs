using System.Text.Json;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

public sealed class SchemaMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly FolderDeckPaths _paths;
    private readonly WorkspaceStore _store;

    public SchemaMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.MigrationTests", Guid.NewGuid().ToString("N"));
        _paths = new FolderDeckPaths(_root);
        _paths.EnsureCreated();
        _store = new WorkspaceStore(_paths, new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(10)));
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {

        }
    }

    private Guid WriteV1File()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "title": "구버전 워크스페이스",
          "description": "v1 파일",
          "lastUsed": "2026-01-02T03:04:05+09:00",
          "folders": [
            {
              "id": "{{Guid.NewGuid()}}",
              "path": "D:\\work\\src",
              "displayName": "src",
              "viewMode": "details",
              "sortBy": "name",
              "sortDesc": false,
              "pinned": true
            }
          ]
        }
        """;

        File.WriteAllText(_paths.WorkspaceFile(id), json);
        return id;
    }

    private Guid WriteV4File()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": 4,
          "id": "{{id}}",
          "title": "v4 워크스페이스",
          "lastUsed": "2026-05-06T07:08:09+09:00",
          "folders": [
            {
              "id": "{{Guid.NewGuid()}}",
              "path": "D:\\work\\src",
              "displayName": "src",
              "viewMode": "details",
              "sortBy": "name",
              "sortDesc": false,
              "pinned": true
            }
          ],
          "grid": { "cols": 4, "rows": 4 },
          "askOnConflict": false,
          "onConflict": "skip"
        }
        """;

        File.WriteAllText(_paths.WorkspaceFile(id), json);
        return id;
    }

    private Guid WriteV5File()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": 5,
          "id": "{{id}}",
          "title": "v5 워크스페이스",
          "lastUsed": "2026-06-07T08:09:10+09:00",
          "folders": [
            {
              "id": "{{Guid.NewGuid()}}",
              "path": "D:\\work\\src",
              "displayName": "src",
              "viewMode": "details",
              "sortBy": "modified",
              "sortDesc": true,
              "pinned": true
            }
          ],
          "leftLayout": { "foldersCollapsed": true, "lowerCollapsed": false },
          "grid": { "cols": 4, "rows": 4 },
          "window": { "x": 10, "y": 20, "width": 800, "height": 600, "maximized": false }
        }
        """;

        File.WriteAllText(_paths.WorkspaceFile(id), json);
        return id;
    }

    private Guid WriteV7File()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": 7,
          "id": "{{id}}",
          "title": "v7 워크스페이스",
          "lastUsed": "2026-06-07T08:09:10+09:00",
          "folders": [
            {
              "id": "{{Guid.NewGuid()}}",
              "path": "D:\\work\\src",
              "displayName": "src",
              "viewMode": "details",
              "sortBy": "modified",
              "sortDesc": true,
              "foldersFirst": false,
              "pinned": true
            }
          ],
          "leftLayout": { "foldersCollapsed": true, "lowerCollapsed": false, "leftPanelCollapsed": true },
          "grid": { "cols": 4, "rows": 4 },
          "window": { "x": 10, "y": 20, "width": 800, "height": 600, "maximized": false }
        }
        """;

        File.WriteAllText(_paths.WorkspaceFile(id), json);
        return id;
    }

    private Guid WriteV8File()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": 8,
          "id": "{{id}}",
          "title": "v8 워크스페이스",
          "lastUsed": "2026-07-08T09:10:11+09:00",
          "folders": [
            {
              "id": "{{Guid.NewGuid()}}",
              "path": "D:\\work\\src",
              "displayName": "src",
              "viewMode": "details",
              "sortBy": "modified",
              "sortDesc": true,
              "foldersFirst": false,
              "showSize": false,
              "showModified": true,
              "pinned": true
            }
          ],
          "leftLayout": { "foldersCollapsed": true, "lowerCollapsed": false, "leftPanelCollapsed": true },
          "grid": { "cols": 4, "rows": 4 },
          "window": { "x": 10, "y": 20, "width": 800, "height": 600, "maximized": false }
        }
        """;

        File.WriteAllText(_paths.WorkspaceFile(id), json);
        return id;
    }

    [Fact]
    public void CurrentSchemaVersionIsEleven()
    {
        Assert.Equal(11, Workspace.CurrentSchemaVersion);
    }

    [Fact]
    public void V1FileLoadsWithoutBeingRejected()
    {
        var id = WriteV1File();

        var result = _store.LoadWorkspace(id);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal("구버전 워크스페이스", result.Value!.Title);
        Assert.Equal(1, result.Value.SchemaVersion);
        Assert.Single(result.Value.Folders);
    }

    [Fact]
    public void MissingLeftLayoutReadsAsNullSoCallersUseDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.LeftLayout);
    }

    [Fact]
    public void SavingAV1FilePromotesItToTheCurrentVersion()
    {
        var id = WriteV1File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(_store.SaveWorkspace(workspace));

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(Workspace.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.Equal("구버전 워크스페이스", reloaded.Title);
        Assert.Single(reloaded.Folders);
    }

    [Fact]
    public void MissingGridAndTilesReadAsNullSoCallersDerive()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.Grid);
        Assert.Null(workspace.Tiles);
        Assert.Null(workspace.Folders[0].RotationSlot);
    }

    [Fact]
    public void DerivingTilesFromAV1FilePromotesItToTheCurrentVersion()
    {
        var id = WriteV1File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.True(GridLayout.EnsureTiles(workspace));
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
        Assert.Contains("\"tiles\"", json);
        Assert.Contains("\"rotationIndex\": 1", json);
        Assert.Contains("\"kind\": \"rotating\"", json);
        Assert.Contains("\"cols\": 4", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Equal(2, reloaded.Tiles!.Count);
        Assert.Equal(workspace.Folders[0].Id, reloaded.Tiles[0].FolderId);
        Assert.Equal(TileKind.Rotating, reloaded.Tiles[1].Kind);
        Assert.Null(reloaded.Tiles[1].FolderId);
    }

    [Fact]
    public void TilesAndRotationSlotRoundTrip()
    {
        var folder = new FolderEntry { Path = @"D:\work\logs", RotationSlot = 2 };
        var workspace = new Workspace
        {
            Title = "배치 저장",
            Folders = [folder],
            Grid = new GridSpec(),
            Tiles =
            [
                new TileSpec
                {
                    Kind = TileKind.Pinned, FolderId = folder.Id,
                    CellX = 1, CellY = 2, SpanX = 2, SpanY = 2,
                },
                new TileSpec { Kind = TileKind.Rotating, CellX = 0, CellY = 0, RotationIndex = 2 },
            ],
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.Equal(2, reloaded.Folders[0].RotationSlot);
        Assert.Equal(4, reloaded.Grid!.Cols);

        var pinned = reloaded.Tiles![0];
        Assert.Equal(folder.Id, pinned.FolderId);
        Assert.Equal((1, 2, 2, 2), (pinned.CellX, pinned.CellY, pinned.SpanX, pinned.SpanY));
        Assert.Null(pinned.RotationIndex);

        Assert.Equal(2, reloaded.Tiles[1].RotationIndex);
        Assert.Null(reloaded.Tiles[1].FolderId);
    }

    [Fact]
    public void RotationSlotIsOmittedWhenNull()
    {
        var workspace = new Workspace
        {
            Title = "기본 순환",
            Folders = [new FolderEntry { Path = @"D:\work\src" }],
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.DoesNotContain("rotationSlot", json);
        Assert.DoesNotContain("tiles", json);
    }

    [Fact]
    public void V1FileAppearsInTheListingAlongsideV2Files()
    {
        WriteV1File();
        var v2 = new Workspace { Title = "새 워크스페이스" };
        Assert.Null(_store.SaveWorkspace(v2));

        var listing = _store.ListWorkspaces();

        Assert.Equal(2, listing.Workspaces.Count);
        Assert.Empty(listing.Failures);
    }

    [Fact]
    public void LeftLayoutRoundTrips()
    {
        var workspace = new Workspace
        {
            Title = "접힘 저장",
            LeftLayout = new LeftLayoutSpec { FoldersCollapsed = true, LowerCollapsed = false },
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.True(reloaded.LeftLayout!.FoldersCollapsed);
        Assert.False(reloaded.LeftLayout.LowerCollapsed);
    }

    [Fact]
    public void LeftLayoutIsOmittedWhenNull()
    {
        var workspace = new Workspace { Title = "슬롯 없음" };
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.DoesNotContain("leftLayout", json);
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
    }

    [Fact]
    public void MissingConflictSlotsReadAsNullSoCallersAskFirst()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.OnConflict);
        Assert.Null(workspace.AskOnConflict);
    }

    [Fact]
    public void ConflictSlotsRoundTrip()
    {
        var workspace = new Workspace
        {
            Title = "충돌 설정",
            OnConflict = ConflictPolicy.Skip,
            AskOnConflict = false,
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.Contains("\"onConflict\": \"skip\"", json);
        Assert.Contains("\"askOnConflict\": false", json);

        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();
        Assert.Equal(ConflictPolicy.Skip, reloaded.OnConflict);
        Assert.False(reloaded.AskOnConflict);
    }

    [Fact]
    public void AV4FileWithoutTheWindowSlotStillLoads()
    {
        var id = WriteV4File();

        var result = _store.LoadWorkspace(id);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(4, result.Value!.SchemaVersion);
        Assert.Null(result.Value.Window);
        Assert.Equal(ConflictPolicy.Skip, result.Value.OnConflict);
    }

    [Fact]
    public void SavingAV4FilePromotesItToTheCurrentVersionAndWritesTheWindowSlot()
    {
        var id = WriteV4File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        workspace.Window = new WindowSpec { X = -1340, Y = -171, Width = 1200, Height = 2000 };
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
        Assert.Contains("\"window\"", json);
        Assert.Contains("\"x\": -1340", json);
        Assert.Contains("\"maximized\": false", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(Workspace.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.Equal(-1340, reloaded.Window!.X);
        Assert.Equal(2000, reloaded.Window.Height);
        Assert.False(reloaded.Window.Maximized);
    }

    [Fact]
    public void TheWindowSlotRoundTripsIncludingMaximized()
    {
        var workspace = new Workspace
        {
            Title = "창 위치 저장",
            Window = new WindowSpec { X = 300.5, Y = 200.25, Width = 1400, Height = 900, Maximized = true },
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();

        Assert.Equal(300.5, reloaded.Window!.X);
        Assert.Equal(200.25, reloaded.Window.Y);
        Assert.True(reloaded.Window.Maximized);
    }

    [Fact]
    public void MonitorIdIsNotWritten()
    {
        var workspace = new Workspace
        {
            Title = "모니터 식별자 없음",
            Window = new WindowSpec { X = 0, Y = 0, Width = 1400, Height = 900 },
        };

        Assert.Null(_store.SaveWorkspace(workspace));

        Assert.DoesNotContain("monitorId", File.ReadAllText(_paths.WorkspaceFile(workspace.Id)));
    }

    [Fact]
    public void TheWindowSlotIsOmittedWhenThereIsNoSavedPosition()
    {
        var workspace = new Workspace { Title = "창 위치 없음" };

        Assert.Null(_store.SaveWorkspace(workspace));

        Assert.DoesNotContain("\"window\"", File.ReadAllText(_paths.WorkspaceFile(workspace.Id)));
    }

    [Fact]
    public void MacrosRoundTripWithTheirEnumsAsStrings()
    {
        var folderId = Guid.NewGuid();
        var workspace = new Workspace
        {
            Title = "매크로 저장",
            Macros =
            [
                new MacroDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "산출물로 복사",
                    Op = FileOperationKind.Copy,
                    Source = new MacroSource { Kind = MacroSourceKind.FixedPath, Path = @"D:\a\b.txt" },
                    Dest = new MacroDest { Kind = MacroDestKind.FolderIds, FolderIds = [folderId] },
                    OnConflict = ConflictPolicy.Rename,
                    Confirm = false,
                },
            ],
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.Contains("\"op\": \"copy\"", json);
        Assert.Contains("\"kind\": \"fixedPath\"", json);
        Assert.Contains("\"kind\": \"folderIds\"", json);
        Assert.Contains("\"onConflict\": \"rename\"", json);

        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();
        var macro = Assert.Single(reloaded.Macros!);

        Assert.Equal("산출물로 복사", macro.Name);
        Assert.Equal(MacroSourceKind.FixedPath, macro.Source!.Kind);
        Assert.Equal(@"D:\a\b.txt", macro.Source.Path);
        Assert.Equal(folderId, Assert.Single(macro.Dest!.FolderIds!));
        Assert.Equal(ConflictPolicy.Rename, macro.OnConflict);
        Assert.False(macro.Confirm);
    }

    [Fact]
    public void MacrosAreOmittedWhenThereAreNone()
    {
        var workspace = new Workspace { Title = "매크로 없음" };

        Assert.Null(_store.SaveWorkspace(workspace));

        Assert.DoesNotContain("macros", File.ReadAllText(_paths.WorkspaceFile(workspace.Id)));
    }

    [Fact]
    public void ConflictSlotsAreOmittedWhenDefault()
    {
        var workspace = new Workspace { Title = "기본 충돌 설정" };

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.DoesNotContain("onConflict", json);
        Assert.DoesNotContain("askOnConflict", json);
    }

    [Fact]
    public void LeftLayoutIsWrittenWithCamelCaseNames()
    {
        var workspace = new Workspace
        {
            Title = "이름 확인",
            LeftLayout = new LeftLayoutSpec { FoldersCollapsed = true, LowerCollapsed = true },
        };

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));

        Assert.Contains("\"leftLayout\"", json);
        Assert.Contains("\"foldersCollapsed\": true", json);
        Assert.Contains("\"lowerCollapsed\": true", json);
    }

    [Fact]
    public void FutureSchemaVersionIsStillRejected()
    {
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(
            new { schemaVersion = Workspace.CurrentSchemaVersion + 1, id, title = "미래", folders = Array.Empty<object>() },
            FolderDeckJson.Options);
        File.WriteAllText(_paths.WorkspaceFile(id), json);

        var result = _store.LoadWorkspace(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, result.Failure!.Kind);
    }

    [Fact]
    public void ShippedSampleWorkspaceStillLoads()
    {

        var sampleDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "workspaces");

        if (!Directory.Exists(sampleDir))
        {
            return;
        }

        foreach (var sample in Directory.EnumerateFiles(sampleDir, "*.json"))
        {
            var copied = Path.Combine(_paths.WorkspacesDir, Path.GetFileName(sample));
            File.Copy(sample, copied, overwrite: true);
        }

        var listing = _store.ListWorkspaces();

        Assert.Empty(listing.Failures);
        Assert.NotEmpty(listing.Workspaces);
    }

    [Fact]
    public void AV5FileWithoutTheNewSlotsStillLoads()
    {
        var id = WriteV5File();

        var result = _store.LoadWorkspace(id);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(5, result.Value!.SchemaVersion);
        Assert.Equal("v5 워크스페이스", result.Value.Title);
    }

    [Fact]
    public void AMissingLeftPanelCollapsedReadsAsExpanded()
    {
        var id = WriteV5File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.False(workspace.LeftLayout!.LeftPanelCollapsed);
        Assert.True(workspace.LeftLayout.FoldersCollapsed);
        Assert.False(workspace.LeftLayout.LowerCollapsed);
    }

    [Fact]
    public void AMissingFoldersFirstReadsAsOn()
    {
        var id = WriteV5File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.True(Assert.Single(workspace.Folders).FoldersFirst);
    }

    [Fact]
    public void AV1FileAlsoReadsTheNewSlotsAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.LeftLayout);
        Assert.True(Assert.Single(workspace.Folders).FoldersFirst);
    }

    [Fact]
    public void SavingAV5FilePromotesItToTheCurrentVersionAndWritesTheNewSlots()
    {
        var id = WriteV5File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        workspace.LeftLayout!.LeftPanelCollapsed = true;
        workspace.Folders[0].FoldersFirst = false;
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
        Assert.Contains("\"leftPanelCollapsed\": true", json);
        Assert.Contains("\"foldersFirst\": false", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(Workspace.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.True(reloaded.LeftLayout!.LeftPanelCollapsed);
        Assert.False(reloaded.Folders[0].FoldersFirst);

        Assert.True(reloaded.LeftLayout.FoldersCollapsed);
        Assert.False(reloaded.LeftLayout.LowerCollapsed);
    }

    [Fact]
    public void TheNewSlotsAreWrittenEvenAtTheirDefaults()
    {
        var id = WriteV5File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"leftPanelCollapsed\": false", json);
        Assert.Contains("\"foldersFirst\": true", json);
    }

    [Fact]
    public void AVersionAboveTheCurrentOneIsStillRejected()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": {{Workspace.CurrentSchemaVersion + 1}},
          "id": "{{id}}",
          "title": "미래 파일",
          "folders": []
        }
        """);

        var result = _store.LoadWorkspace(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, result.Failure!.Kind);
    }

    [Fact]
    public void ASearchKindTileReadsAsTileKindSearch()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 7,
          "id": "{{id}}",
          "title": "검색 타일",
          "folders": [],
          "tiles": [ { "kind": "search", "cellX": 0, "cellY": 0, "spanX": 1, "spanY": 1 } ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var tile = Assert.Single(workspace.Tiles!);
        Assert.Equal(TileKind.Search, tile.Kind);
    }

    [Fact]
    public void ASearchTileWithAFolderIdIsReadWithTheFolderIdCleared()
    {
        var id = Guid.NewGuid();
        var strayFolderId = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 7,
          "id": "{{id}}",
          "title": "검색 타일 folderId",
          "folders": [],
          "tiles": [ { "kind": "search", "folderId": "{{strayFolderId}}", "cellX": 0, "cellY": 0 } ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var tile = Assert.Single(workspace.Tiles!);
        Assert.Null(tile.FolderId);
    }

    [Fact]
    public void DuplicateSearchTilesKeepOnlyTheFirst()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 7,
          "id": "{{id}}",
          "title": "검색 타일 중복",
          "folders": [],
          "tiles": [
            { "kind": "pinned", "cellX": 0, "cellY": 0 },
            { "kind": "search", "cellX": 1, "cellY": 0 },
            { "kind": "search", "cellX": 2, "cellY": 0 },
            { "kind": "search", "cellX": 3, "cellY": 0 }
          ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Equal(2, workspace.Tiles!.Count);
        var remainingSearch = Assert.Single(workspace.Tiles, t => t.Kind == TileKind.Search);
        Assert.Equal(1, remainingSearch.CellX);
    }

    [Fact]
    public void SavingStampsWorkspaceSchemaVersionEleven()
    {
        var workspace = new Workspace { Title = "버전 찍힘" };

        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));
        Assert.Contains("\"schemaVersion\": 11", json);
    }

    [Fact]
    public void AMissingShowSizeAndShowModifiedReadAsOn()
    {
        var id = WriteV7File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.True(folder.ShowSize);
        Assert.True(folder.ShowModified);
    }

    [Fact]
    public void AV1FileAlsoReadsShowSizeAndShowModifiedAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.True(folder.ShowSize);
        Assert.True(folder.ShowModified);
    }

    [Fact]
    public void SavingAV7FilePromotesItToTheCurrentVersionAndWritesTheNewSlots()
    {
        var id = WriteV7File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        workspace.Folders[0].ShowSize = false;
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
        Assert.Contains("\"showSize\": false", json);
        Assert.Contains("\"showModified\": true", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(Workspace.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.False(reloaded.Folders[0].ShowSize);
        Assert.True(reloaded.Folders[0].ShowModified);

        Assert.False(reloaded.Folders[0].FoldersFirst);
    }

    [Fact]
    public void TheNewV8SlotsAreWrittenEvenAtTheirDefaults()
    {
        var id = WriteV7File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"showSize\": true", json);
        Assert.Contains("\"showModified\": true", json);
    }

    [Fact]
    public void AMissingShowPreviewReadsAsOff()
    {
        var id = WriteV8File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.False(folder.ShowPreview);
    }

    [Fact]
    public void AMissingPreviewRatioReadsAsTheDefault()
    {
        var id = WriteV8File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, folder.PreviewRatio);
    }

    [Fact]
    public void AV1FileAlsoReadsTheV9SlotsAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.False(folder.ShowPreview);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, folder.PreviewRatio);
    }

    [Fact]
    public void SavingAV8FilePromotesItToV11AndWritesTheV9Slots()
    {
        var id = WriteV8File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        workspace.Folders[0].ShowPreview = true;
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"schemaVersion\": 11", json);
        Assert.Contains("\"showPreview\": true", json);
        Assert.Contains("\"previewRatio\": 0.35", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(11, reloaded.SchemaVersion);
        Assert.True(reloaded.Folders[0].ShowPreview);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, reloaded.Folders[0].PreviewRatio);

        Assert.False(reloaded.Folders[0].ShowSize);
    }

    [Fact]
    public void TheNewV9SlotsAreWrittenEvenAtTheirDefaults()
    {
        var id = WriteV8File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"showPreview\": false", json);
        Assert.Contains("\"previewRatio\": 0.35", json);
    }

    [Fact]
    public void APreviewRatioOutsideTheRangeFallsBackToTheDefault()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 9,
          "id": "{{id}}",
          "title": "범위 밖 previewRatio",
          "folders": [
            { "id": "{{Guid.NewGuid()}}", "path": "D:\\work\\a", "previewRatio": 1.5 },
            { "id": "{{Guid.NewGuid()}}", "path": "D:\\work\\b", "previewRatio": -0.2 }
          ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Equal(FolderEntry.DefaultPreviewRatio, workspace.Folders[0].PreviewRatio);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, workspace.Folders[1].PreviewRatio);
    }

    [Fact]
    public void AV9FileKeepsItsExistingViewMode()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 9,
          "id": "{{id}}",
          "title": "v9 보기 방식",
          "folders": [
            { "id": "{{Guid.NewGuid()}}", "path": "D:\\work\\a", "viewMode": "details" }
          ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Equal(FolderViewMode.Details, Assert.Single(workspace.Folders).ViewMode);
    }

    [Fact]
    public void LargeIconsLoadsAndSurvivesASave()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 10,
          "id": "{{id}}",
          "title": "큰 아이콘",
          "folders": [
            { "id": "{{Guid.NewGuid()}}", "path": "D:\\work\\a", "viewMode": "largeIcons" }
          ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(FolderViewMode.LargeIcons, Assert.Single(workspace.Folders).ViewMode);

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"schemaVersion\": 11", json);
        Assert.Contains("\"viewMode\": \"largeIcons\"", json);
    }

    [Fact]
    public void ExtraLargeIconsLoadsAndSurvivesASave()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.WorkspaceFile(id), $$"""
        {
          "schemaVersion": 11,
          "id": "{{id}}",
          "title": "아주 큰 아이콘",
          "folders": [
            { "id": "{{Guid.NewGuid()}}", "path": "D:\\work\\a", "viewMode": "extraLargeIcons" }
          ]
        }
        """);

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(FolderViewMode.ExtraLargeIcons, Assert.Single(workspace.Folders).ViewMode);

        Assert.Null(_store.SaveWorkspace(workspace));
        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"schemaVersion\": 11", json);
        Assert.Contains("\"viewMode\": \"extraLargeIcons\"", json);
    }
}
