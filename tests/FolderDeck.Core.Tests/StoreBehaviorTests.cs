using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>열거·삭제·설정·디바운스·경로 규약.</summary>
public sealed class StoreBehaviorTests
{
    // ── 열거 (§3.1 — 인덱스 파일이 아니라 workspaces\*.json) ──────────────────

    [Fact]
    public void ListingIsEmptyWhenNothingIsSavedYet()
    {
        using var temp = new TempStore();

        var listing = temp.Store.ListWorkspaces();

        Assert.Empty(listing.Workspaces);
        Assert.Empty(listing.Failures);
    }

    [Fact]
    public void ListingSurvivesAMissingWorkspacesDirectory()
    {
        using var temp = new TempStore();
        Directory.Delete(temp.Paths.WorkspacesDir, recursive: true);

        var listing = temp.Store.ListWorkspaces();

        Assert.Empty(listing.Workspaces);
        Assert.Empty(listing.Failures);
    }

    [Fact]
    public void ListingPicksUpEveryWorkspaceFile()
    {
        using var temp = new TempStore();
        var ids = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var workspace = new Workspace { Title = $"작업 {i}" };
                temp.Store.SaveWorkspace(workspace);
                return workspace.Id;
            })
            .ToHashSet();

        var listing = temp.Store.ListWorkspaces();

        Assert.Empty(listing.Failures);
        Assert.Equal(ids, listing.Workspaces.Select(w => w.Id).ToHashSet());
    }

    [Fact]
    public void ListingIgnoresNonJsonFiles()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "정상" };
        temp.Store.SaveWorkspace(workspace);
        File.WriteAllText(Path.Combine(temp.Paths.WorkspacesDir, "readme.txt"), "메모");
        File.WriteAllText(Path.Combine(temp.Paths.WorkspacesDir, "backup.json.bak"), "{ 깨진");
        File.WriteAllText(Path.Combine(temp.Paths.WorkspacesDir, $"{Guid.NewGuid()}.json.tmp"), "{ 깨진");

        var listing = temp.Store.ListWorkspaces();

        Assert.Single(listing.Workspaces);
        Assert.Empty(listing.Failures);
    }

    // ── 삭제 (§3.1 — 워크스페이스만) ─────────────────────────────────────────

    [Fact]
    public void DeleteRemovesOnlyTheWorkspaceFileNotTheRealFolders()
    {
        using var temp = new TempStore();
        var realFolder = Path.Combine(temp.Root, "real-folder");
        Directory.CreateDirectory(realFolder);
        File.WriteAllText(Path.Combine(realFolder, "keep.txt"), "그대로 있어야 한다");

        var workspace = new Workspace
        {
            Title = "지울 작업",
            Folders = [new FolderEntry { Path = realFolder }],
        };
        temp.Store.SaveWorkspace(workspace);

        Assert.Null(temp.Store.DeleteWorkspace(workspace.Id));

        Assert.False(File.Exists(temp.Paths.WorkspaceFile(workspace.Id)));
        Assert.Empty(temp.Store.ListWorkspaces().Workspaces);
        Assert.True(Directory.Exists(realFolder));
        Assert.Equal("그대로 있어야 한다", File.ReadAllText(Path.Combine(realFolder, "keep.txt")));
    }

    [Fact]
    public void DeletingSomethingThatIsNotThereReportsNotFound()
    {
        using var temp = new TempStore();

        var failure = temp.Store.DeleteWorkspace(Guid.NewGuid());

        Assert.Equal(StorageFailureKind.NotFound, failure!.Kind);
    }

    // ── 설정 (§7.2) ──────────────────────────────────────────────────────────

    [Fact]
    public void FirstRunSettingsReportNotFoundSoTheCallerCanChooseDefaults()
    {
        using var temp = new TempStore();

        var result = temp.Store.LoadSettings();

        Assert.False(result.IsSuccess);
        Assert.Equal(StorageFailureKind.NotFound, result.Failure!.Kind);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        using var temp = new TempStore();
        var lastId = Guid.NewGuid();
        var order = new List<Guid> { lastId, Guid.NewGuid() };

        Assert.Null(temp.Store.SaveSettings(new AppSettings
        {
            SkipLauncher = true,
            LastWorkspaceId = lastId,
            WorkspaceOrder = order,
        }));

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Equal(AppSettings.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.True(reloaded.SkipLauncher);
        Assert.Equal(lastId, reloaded.LastWorkspaceId);
        Assert.Equal(order, reloaded.WorkspaceOrder);
    }

    // ── 확장자별 글리프 매핑 (D59) — 사전형을 JSON 에 담아 본 첫 선례(B-0 Q3) ─────────

    [Fact]
    public void ExtensionGlyphsRoundTrip()
    {
        using var temp = new TempStore();
        var mapping = new Dictionary<string, string> { [".pdf"] = "📕", [".zip"] = "🗜" };

        Assert.Null(temp.Store.SaveSettings(new AppSettings { ExtensionGlyphs = mapping }));

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Equal(3, reloaded.SchemaVersion);
        Assert.Equal(mapping.Count, reloaded.ExtensionGlyphs.Count);
        foreach (var (key, value) in mapping)
        {
            Assert.Equal(value, reloaded.ExtensionGlyphs[key]);
        }
    }

    /// <summary>
    /// 슬롯이 없는 옛(v2) 파일 — <c>null</c> 이 흘러 <c>NullReferenceException</c> 이 나지
    /// 않고 빈 매핑으로 읽혀야 한다(D59 「만들 것」).
    /// </summary>
    [Fact]
    public void MissingExtensionGlyphsSlotReadsAsEmpty()
    {
        using var temp = new TempStore();
        File.WriteAllText(temp.Paths.SettingsFile, "{ \"schemaVersion\": 2, \"skipLauncher\": false }");

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Empty(reloaded.ExtensionGlyphs);
    }

    /// <summary>슬롯은 있되 값이 명시적으로 <c>null</c> 인 파일도 같은 자리에서 닫힌다.</summary>
    [Fact]
    public void ExplicitNullExtensionGlyphsReadsAsEmpty()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.Paths.SettingsFile, "{ \"schemaVersion\": 2, \"extensionGlyphs\": null }");

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Empty(reloaded.ExtensionGlyphs);
    }

    /// <summary>
    /// 넣을 때·찾을 때가 같은 정규화를 거친다(D59 결정 5) — <c>PDF</c> · <c>.PDF</c> ·
    /// <c>.pdf</c> 가 전부 같은 키로 떨어진다. 빈 문자열·공백은 "매핑 못 함"을 뜻하는 빈
    /// 문자열로, 점 하나(<c>"."</c>)는 유효한 키 그대로 돌아온다.
    /// </summary>
    [Fact]
    public void NormalizeExtensionLowersAndForcesLeadingDot()
    {
        Assert.Equal(".pdf", AppSettings.NormalizeExtension("PDF"));
        Assert.Equal(".pdf", AppSettings.NormalizeExtension(".PDF"));
        Assert.Equal(".pdf", AppSettings.NormalizeExtension(".pdf"));
        Assert.Equal(".pdf", AppSettings.NormalizeExtension("  .pdf  "));
        Assert.Equal(string.Empty, AppSettings.NormalizeExtension(string.Empty));
        Assert.Equal(string.Empty, AppSettings.NormalizeExtension("   "));
        Assert.Equal(".", AppSettings.NormalizeExtension("."));
    }

    [Fact]
    public void SettingsOmitNullOptionalFields()
    {
        using var temp = new TempStore();

        temp.Store.SaveSettings(new AppSettings());
        var json = File.ReadAllText(temp.Paths.SettingsFile);

        Assert.DoesNotContain("lastWorkspaceId", json);
        Assert.DoesNotContain("workspaceOrder", json);
        Assert.Contains("\"skipLauncher\": false", json);
    }

    [Fact]
    public void CorruptSettingsFailInsteadOfResetting()
    {
        using var temp = new TempStore();
        File.WriteAllText(temp.Paths.SettingsFile, "{ \"skipLauncher\": ");

        var result = temp.Store.LoadSettings();

        Assert.Equal(StorageFailureKind.CorruptData, result.Failure!.Kind);
    }

    [Fact]
    public void FutureSettingsSchemaVersionFails()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.Paths.SettingsFile, $"{{ \"schemaVersion\": {AppSettings.CurrentSchemaVersion + 1} }}");

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, temp.Store.LoadSettings().Failure!.Kind);
    }

    [Fact]
    public void SettingsWithoutSchemaVersionFails()
    {
        using var temp = new TempStore();
        File.WriteAllText(temp.Paths.SettingsFile, "{ \"skipLauncher\": true }");

        Assert.Equal(StorageFailureKind.InvalidContent, temp.Store.LoadSettings().Failure!.Kind);
    }

    // ── 디바운스 저장 (§5) ───────────────────────────────────────────────────

    [Fact]
    public void DebouncedSaveCollapsesRapidChangesIntoTheLastOne()
    {
        using var temp = new TempStore(debounce: TimeSpan.FromMilliseconds(60));
        var workspace = new Workspace { Title = "타이핑 중" };

        for (var i = 0; i < 10; i++)
        {
            workspace.Title = $"제목 {i}";
            temp.Store.SaveWorkspaceDebounced(workspace);
        }

        // 아직 조용해지지 않았으니 파일이 없다.
        Assert.False(File.Exists(temp.Paths.WorkspaceFile(workspace.Id)));

        temp.Store.Flush();

        Assert.Equal("제목 9", temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow().Title);
        Assert.Empty(temp.SaveFailures);
    }

    [Fact]
    public async Task DebouncedSaveWritesOnceItGoesQuiet()
    {
        using var temp = new TempStore(debounce: TimeSpan.FromMilliseconds(40));
        var workspace = new Workspace { Title = "가만히 두면 저장" };

        temp.Store.SaveWorkspaceDebounced(workspace);

        var target = temp.Paths.WorkspaceFile(workspace.Id);
        var deadline = TimeSpan.FromSeconds(5);
        var waited = TimeSpan.Zero;
        while (!File.Exists(target) && waited < deadline)
        {
            await Task.Delay(25);
            waited += TimeSpan.FromMilliseconds(25);
        }

        Assert.True(File.Exists(target), "디바운스가 지나면 저장돼 있어야 한다.");
        Assert.Equal("가만히 두면 저장", temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow().Title);
    }

    /// <summary>예약 시점의 스냅샷이 저장된다 — 타이머 도는 동안의 중간 변경이 섞이지 않는다.</summary>
    [Fact]
    public void DebouncedSaveSnapshotsAtScheduleTime()
    {
        using var temp = new TempStore(debounce: TimeSpan.FromMilliseconds(200));
        var workspace = new Workspace { Title = "예약 당시" };

        temp.Store.SaveWorkspaceDebounced(workspace);
        workspace.Title = "예약 후 변경";
        temp.Store.Flush();

        Assert.Equal("예약 당시", temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow().Title);
    }

    /// <summary>
    /// <c>Flush()</c> 는 <b>남이 시작한</b> 쓰기도 기다린다 (D27). 타이머가 이미 발화해
    /// <c>Write</c> 안에 들어와 있으면 사전은 비어 있지만 그 쓰기는 진행 중이다 — 그때
    /// 그냥 돌아가면 호출자가 원자적 교체 전 파일을 읽는다.
    /// </summary>
    /// <remarks>
    /// 타이밍을 재우지 않고 신호로 짠다. 실패해도 굳지 않게 판정은 모아 두고, 기다리는 쪽을
    /// 먼저 풀어 준 뒤에 단정한다 — 안 풀면 <c>Dispose</c> 의 <c>Flush</c> 까지 같이 굳는다.
    /// </remarks>
    [Fact]
    public async Task FlushWaitsForAWriteThatIsAlreadyRunning()
    {
        using var entered = new ManualResetEventSlim(false);
        using var mayLeave = new ManualResetEventSlim(false);
        using var flushReturned = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(20));

        scheduler.Schedule("key", () =>
        {
            entered.Set();
            mayLeave.Wait();
            return null;
        });

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "타이머가 발화하지 않았다.");

        // 사전은 이미 비었고 쓰기는 진행 중이다 — 그것도 "밀린 것"으로 세야 한다.
        Assert.True(scheduler.HasPending, "쓰는 중인 것을 HasPending 이 세지 않는다.");

        var flush = Task.Run(() =>
        {
            scheduler.Flush();
            flushReturned.Set();
        });

        var returnedEarly = flushReturned.Wait(TimeSpan.FromMilliseconds(300));

        mayLeave.Set();
        await flush;

        Assert.False(returnedEarly, "Flush() 가 진행 중인 쓰기를 기다리지 않고 반환했다.");
        Assert.True(flushReturned.IsSet);
    }

    /// <summary>
    /// <c>Dispose()</c> 는 멱등하다 (D29). <c>_disposed</c> 를 맨 앞에서 세우므로 두 번째는
    /// 바로 돌아간다 — 처분한 <c>_idle</c> 을 다시 기다리지 않는다.
    /// </summary>
    [Fact]
    public void DisposingTwiceDoesNotThrow()
    {
        var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(20));
        scheduler.Schedule("key", () => null);

        scheduler.Dispose();
        scheduler.Dispose();
    }

    /// <summary><c>Dispose()</c> 뒤의 예약은 조용히 사라지지 않는다 (D29).</summary>
    [Fact]
    public void SchedulingAfterDisposeThrows()
    {
        var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(20));
        scheduler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scheduler.Schedule("key", () => null));
    }

    [Fact]
    public void DebouncedSettingsSaveWorks()
    {
        using var temp = new TempStore();

        temp.Store.SaveSettingsDebounced(new AppSettings { SkipLauncher = true });
        temp.Store.Flush();

        Assert.True(temp.Store.LoadSettings().ValueOrThrow().SkipLauncher);
    }

    /// <summary>백그라운드 쓰기 실패는 이벤트로 올라온다 — 조용히 사라지지 않는다 (§5).</summary>
    [Fact]
    public void BackgroundWriteFailureIsReported()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "쓸 수 없는 곳" };

        // 대상 경로를 폴더로 막아 쓰기를 실패시킨다.
        Directory.CreateDirectory(temp.Paths.WorkspaceFile(workspace.Id));

        temp.Store.SaveWorkspaceDebounced(workspace);
        temp.Store.Flush();

        var failure = Assert.Single(temp.SaveFailures);
        Assert.Contains(failure.Kind, new[] { StorageFailureKind.IoError, StorageFailureKind.AccessDenied });
    }

    [Fact]
    public void ImmediateSaveFailureIsReturned()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "쓸 수 없는 곳" };
        Directory.CreateDirectory(temp.Paths.WorkspaceFile(workspace.Id));

        var failure = temp.Store.SaveWorkspace(workspace);

        Assert.NotNull(failure);
    }

    // ── 경로 규약 (§7) ───────────────────────────────────────────────────────

    [Fact]
    public void DefaultRootIsUnderAppData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal(Path.Combine(appData, "FolderDeck"), FolderDeckPaths.Default.Root);
        Assert.Equal(Path.Combine(appData, "FolderDeck", "workspaces"), FolderDeckPaths.Default.WorkspacesDir);
        Assert.Equal(Path.Combine(appData, "FolderDeck", "settings.json"), FolderDeckPaths.Default.SettingsFile);
    }

    [Fact]
    public void WorkspaceFileIsNamedAfterItsId()
    {
        var id = Guid.NewGuid();

        Assert.Equal(
            Path.Combine(FolderDeckPaths.Default.WorkspacesDir, $"{id}.json"),
            FolderDeckPaths.Default.WorkspaceFile(id));
    }

    [Fact]
    public void EnsureCreatedMakesBothDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "FolderDeck.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new FolderDeckPaths(root);
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.Root));
            Assert.True(Directory.Exists(paths.WorkspacesDir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
