using System.Text.Json;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>
/// schemaVersion 1 → 2 (<c>leftLayout</c>) → 3 (<c>grid</c>·<c>tiles</c>·<c>rotationSlot</c>)
/// → 4 (<c>onConflict</c>·<c>askOnConflict</c>). 구파일을 거부하지 않는다.
/// </summary>
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
            // 정리 실패는 무시.
        }
    }

    /// <summary>Stage 1 이 쓴 것과 같은 모양의 v1 파일 — leftLayout 슬롯이 아예 없다.</summary>
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

    /// <summary>
    /// Phase 3 이 남긴 것과 같은 모양의 v4 파일 — <c>window</c> 슬롯이 아예 없다.
    /// </summary>
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

    /// <summary>
    /// 6 이 여는 두 슬롯(<c>leftLayout.leftPanelCollapsed</c> · <c>folders[].foldersFirst</c>)만
    /// 없는 <b>바로 앞 버전</b> 파일. <c>leftLayout</c> 은 안쪽 둘만 들고 있다.
    /// </summary>
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

    /// <summary>
    /// 8 이 여는 두 슬롯(<c>folders[].showSize</c> · <c>folders[].showModified</c>)만 없는
    /// <b>바로 앞 버전</b> 파일.
    /// </summary>
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

    /// <summary>Phase 15 가 남긴 것과 같은 모양의 v8 파일 — 미리보기 슬롯 둘이 아예 없다.</summary>
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
    public void CurrentSchemaVersionIsTen()
    {
        Assert.Equal(10, Workspace.CurrentSchemaVersion);
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

    // ── v2 → v3: grid / tiles / rotationSlot ─────────────────────────────────

    [Fact]
    public void MissingGridAndTilesReadAsNullSoCallersDerive()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.Grid);
        Assert.Null(workspace.Tiles);
        Assert.Null(workspace.Folders[0].RotationSlot);
    }

    /// <summary>구파일을 열어 배치를 유도하고 저장하면 최신 버전 파일이 된다.</summary>
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

        // 상시 폴더 1개 → 상시 타일 1개 + 순환 타일 1개.
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

    /// <summary>기본값(순환1)은 파일에 남기지 않는다 — 손으로 읽을 때 소음이 된다.</summary>
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

    // ── v3 → v4: onConflict / askOnConflict (§4.3) ───────────────────────────

    /// <summary>
    /// 슬롯이 없는 구파일은 "묻는다 · 덮어쓴다"로 읽혀야 한다. <c>askOnConflict</c> 를
    /// <c>bool</c> 로 뒀다면 여기서 <c>false</c> 가 되어 <b>묻지 않고 덮어쓰는</b> 상태가 됐다.
    /// </summary>
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

        // enum 은 camelCase 문자열로 나간다 — 손으로 열어 읽을 수 있어야 한다.
        Assert.Contains("\"onConflict\": \"skip\"", json);
        Assert.Contains("\"askOnConflict\": false", json);

        var reloaded = _store.LoadWorkspace(workspace.Id).ValueOrThrow();
        Assert.Equal(ConflictPolicy.Skip, reloaded.OnConflict);
        Assert.False(reloaded.AskOnConflict);
    }

    // ── v4 → v5: window (§6.1) ───────────────────────────────────────────────

    /// <summary>
    /// <c>window</c> 슬롯이 없는 v4 파일은 오류 없이 열리고, 창 위치는 "저장된 것 없음"이 된다
    /// — 호출부가 기본 위치를 쓴다.
    /// </summary>
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

    /// <summary>창을 옮겨 저장하면 그 파일이 v5 로 승격된다 (§7.1 스키마 규칙).</summary>
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

    /// <summary>
    /// <c>monitorId</c> 는 쓰지 않기로 했다(Stage 0 §4). null 이면 파일에 남지 않는다 —
    /// 판정에 안 쓰는 슬롯이 파일에 있으면 나중에 누군가 판정에 끌어들인다.
    /// </summary>
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

    // ── 매크로 슬롯 (§4.7) ───────────────────────────────────────────────────

    /// <summary>
    /// 매크로는 v1 부터 모델에 자리가 있었고 Stage 5 에서 실제로 쓰기 시작했다 —
    /// 필드가 늘지 않았으므로 스키마 버전은 그대로다.
    /// </summary>
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

    /// <summary>기본값이면 슬롯을 남기지 않는다 — 아무 뜻도 없는 줄을 파일에 두지 않는다.</summary>
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

    /// <summary>미래 버전은 여전히 하드 실패다 — 모르는 형식을 추측해서 열지 않는다.</summary>
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
        // samples\workspaces\*.json 은 v1 로 만들어 뒀다. 계속 열려야 한다.
        var sampleDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "workspaces");

        if (!Directory.Exists(sampleDir))
        {
            return;   // 소스 트리 밖에서 돌린 경우.
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

    // ── schemaVersion 6 (Phase 8-E ①) ────────────────────────────────────────
    //
    // 두 슬롯이 늘었다: leftLayout.leftPanelCollapsed · folders[].foldersFirst.
    // 둘 다 <b>필드 추가뿐</b>이라 구파일을 거부하지 않는다 — 없으면 기본값으로 읽히고
    // 그 기본값이 8-E 전의 실제 동작이다. 그래서 마이그레이션 코드가 없다.

    [Fact]
    public void AV5FileWithoutTheNewSlotsStillLoads()
    {
        var id = WriteV5File();

        var result = _store.LoadWorkspace(id);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(5, result.Value!.SchemaVersion);
        Assert.Equal("v5 워크스페이스", result.Value.Title);
    }

    /// <summary>
    /// 왼쪽 패널 통째 접힘이 없으면 <b>펼침</b>으로 읽힌다 — 8-E 전에는 늘 펴진 채로 열렸다.
    /// 같은 칸의 안쪽 두 값은 파일에 적힌 그대로다.
    /// </summary>
    [Fact]
    public void AMissingLeftPanelCollapsedReadsAsExpanded()
    {
        var id = WriteV5File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.False(workspace.LeftLayout!.LeftPanelCollapsed);
        Assert.True(workspace.LeftLayout.FoldersCollapsed);
        Assert.False(workspace.LeftLayout.LowerCollapsed);
    }

    /// <summary>
    /// <b>「폴더 먼저」가 없으면 켜진 것으로 읽힌다.</b> 여기가 이 회차에서 가장 조심할 자리다 —
    /// 그냥 <c>bool</c> 이면 <c>false</c>(통합 정렬)로 읽혀 구파일의 목록 순서가 조용히 바뀐다.
    /// 모델의 초기화자(<c>= true</c>)가 그것을 막는다.
    /// </summary>
    [Fact]
    public void AMissingFoldersFirstReadsAsOn()
    {
        var id = WriteV5File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.True(Assert.Single(workspace.Folders).FoldersFirst);
    }

    /// <summary>v1 파일도 마찬가지다 — 슬롯이 없던 시절이 더 길다.</summary>
    [Fact]
    public void AV1FileAlsoReadsTheNewSlotsAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.Null(workspace.LeftLayout);
        Assert.True(Assert.Single(workspace.Folders).FoldersFirst);
    }

    /// <summary>저장하면 지금 판(D69-c 기준 7)으로 승격되고 두 슬롯이 파일에 적힌다.</summary>
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

        // 안쪽 두 접힘은 건드리지 않았다.
        Assert.True(reloaded.LeftLayout.FoldersCollapsed);
        Assert.False(reloaded.LeftLayout.LowerCollapsed);
    }

    /// <summary>기본값 그대로여도 파일에는 적힌다 — 형제 슬롯(sortDesc 등)과 같은 규칙이다.</summary>
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

    /// <summary>
    /// 지금 판보다 큰 버전은 여전히 거절이다 — 앞으로 늘 슬롯을 모르는 채로 열면 안 된다.
    /// </summary>
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

    // ── v6 → v7: 검색 타일 (§7.1 v7 · D69-c) ─────────────────────────────────

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

    /// <summary>손으로 값을 남겨 둔 파일도 로드 시점에 지운다(결정 3).</summary>
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

    /// <summary>둘 이상이면 목록 순서로 첫 하나만 남는다 — 거부하지 않는다(결정 4).</summary>
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
    public void SavingStampsWorkspaceSchemaVersionTen()
    {
        var workspace = new Workspace { Title = "버전 찍힘" };

        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(workspace.Id));
        Assert.Contains("\"schemaVersion\": 10", json);
    }

    // ── schemaVersion 8 (Phase 15 · D79) ─────────────────────────────────────
    //
    // 두 슬롯이 늘었다: folders[].showSize · folders[].showModified.
    // 둘 다 필드 추가뿐이라 구파일을 거부하지 않는다 — 없으면 기본값으로 읽히고
    // 그 기본값이 Phase 15 전의 실제 동작이다. 그래서 마이그레이션 코드가 없다.

    /// <summary>
    /// <b>「크기」·「수정」이 없으면 켜진 것으로 읽힌다.</b> 그냥 <c>bool</c> 이면 <c>false</c>
    /// (숨김)로 읽혀 구파일의 두 열이 조용히 사라진다. 모델의 초기화자(<c>= true</c>)가 그것을
    /// 막는다 — v6 <c>foldersFirst</c> 와 같은 함정이다.
    /// </summary>
    [Fact]
    public void AMissingShowSizeAndShowModifiedReadAsOn()
    {
        var id = WriteV7File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.True(folder.ShowSize);
        Assert.True(folder.ShowModified);
    }

    /// <summary>v1 파일도 마찬가지다 — 슬롯이 없던 시절이 더 길다.</summary>
    [Fact]
    public void AV1FileAlsoReadsShowSizeAndShowModifiedAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.True(folder.ShowSize);
        Assert.True(folder.ShowModified);
    }

    /// <summary>저장하면 지금 판(8)으로 승격되고 두 슬롯이 파일에 적힌다.</summary>
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

        // 옆 슬롯(v6 의 foldersFirst)은 건드리지 않았다.
        Assert.False(reloaded.Folders[0].FoldersFirst);
    }

    /// <summary>기본값 그대로여도 파일에는 적힌다 — 형제 슬롯(foldersFirst 등)과 같은 규칙이다.</summary>
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

    // ── schemaVersion 9 (Phase 17 · D86) ─────────────────────────────────────
    //
    // 두 슬롯이 늘었다: folders[].showPreview · folders[].previewRatio.
    // 둘 다 필드 추가뿐이라 구파일을 거부하지 않는다 — 없으면 기본값으로 읽히고
    // 그 기본값이 Phase 17 전의 실제 동작(미리보기 칸 없음)이라 마이그레이션 코드가 없다.

    /// <summary>
    /// <b>「미리보기」가 없으면 꺼진 것으로 읽힌다.</b> <c>showSize</c>·<c>foldersFirst</c> 와
    /// 방향이 반대다 — 저 둘은 기존 동작이 켜짐이라 <c>= true</c> 였지만, 미리보기 칸은 Phase 17
    /// 전에 없던 기능이라 <c>= false</c> 여야 구파일 동작이 그대로다.
    /// </summary>
    [Fact]
    public void AMissingShowPreviewReadsAsOff()
    {
        var id = WriteV8File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.False(folder.ShowPreview);
    }

    /// <summary>슬롯이 없는 구파일은 미리보기 몫이 기본값(0.35)으로 읽힌다.</summary>
    [Fact]
    public void AMissingPreviewRatioReadsAsTheDefault()
    {
        var id = WriteV8File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, folder.PreviewRatio);
    }

    /// <summary>v1 파일도 마찬가지다 — 슬롯이 없던 시절이 더 길다.</summary>
    [Fact]
    public void AV1FileAlsoReadsTheV9SlotsAsDefaults()
    {
        var id = WriteV1File();

        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        var folder = Assert.Single(workspace.Folders);
        Assert.False(folder.ShowPreview);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, folder.PreviewRatio);
    }

    /// <summary>저장하면 지금 판(10)으로 승격되고 v9의 두 슬롯도 파일에 적힌다.</summary>
    [Fact]
    public void SavingAV8FilePromotesItToV10AndWritesTheV9Slots()
    {
        var id = WriteV8File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        workspace.Folders[0].ShowPreview = true;
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"schemaVersion\": 10", json);
        Assert.Contains("\"showPreview\": true", json);
        Assert.Contains("\"previewRatio\": 0.35", json);

        var reloaded = _store.LoadWorkspace(id).ValueOrThrow();
        Assert.Equal(10, reloaded.SchemaVersion);
        Assert.True(reloaded.Folders[0].ShowPreview);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, reloaded.Folders[0].PreviewRatio);

        // 옆 슬롯(v8 의 showSize)은 건드리지 않았다.
        Assert.False(reloaded.Folders[0].ShowSize);
    }

    /// <summary>기본값 그대로여도 파일에는 적힌다 — 형제 슬롯(showSize 등)과 같은 규칙이다.</summary>
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

    /// <summary>손으로 고친 파일에 범위 밖 값이 들어 있으면 기본값으로 떨어진다(§7.1 v9).</summary>
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

    // ── schemaVersion 10 (Phase 21 · D125) ────────────────────────────────────

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
        Assert.Contains("\"schemaVersion\": 10", json);
        Assert.Contains("\"viewMode\": \"largeIcons\"", json);
    }
}
