using System.Text.Json;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>
/// schemaVersion 1 → 2 (<c>leftLayout</c>) → 3 (<c>grid</c>·<c>tiles</c>·<c>rotationSlot</c>).
/// 구파일을 거부하지 않는다.
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

    [Fact]
    public void CurrentSchemaVersionIsThree()
    {
        Assert.Equal(3, Workspace.CurrentSchemaVersion);
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

    /// <summary>구파일을 열어 배치를 유도하고 저장하면 v3 파일이 된다.</summary>
    [Fact]
    public void DerivingTilesFromAV1FilePromotesItToV3()
    {
        var id = WriteV1File();
        var workspace = _store.LoadWorkspace(id).ValueOrThrow();

        Assert.True(GridLayout.EnsureTiles(workspace));
        Assert.Null(_store.SaveWorkspace(workspace));

        var json = File.ReadAllText(_paths.WorkspaceFile(id));
        Assert.Contains("\"schemaVersion\": 3", json);
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
        Assert.Contains("\"schemaVersion\": 3", json);
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
            new { schemaVersion = 99, id, title = "미래", folders = Array.Empty<object>() },
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
}
