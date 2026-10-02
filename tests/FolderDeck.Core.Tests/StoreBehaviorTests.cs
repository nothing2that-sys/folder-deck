using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

public sealed class StoreBehaviorTests
{

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

    [Fact]
    public void ExtensionGlyphsRoundTrip()
    {
        using var temp = new TempStore();
        var mapping = new Dictionary<string, string> { [".pdf"] = "📕", [".zip"] = "🗜" };

        Assert.Null(temp.Store.SaveSettings(new AppSettings { ExtensionGlyphs = mapping }));

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Equal(4, reloaded.SchemaVersion);
        Assert.Equal(mapping.Count, reloaded.ExtensionGlyphs.Count);
        foreach (var (key, value) in mapping)
        {
            Assert.Equal(value, reloaded.ExtensionGlyphs[key]);
        }
    }

    [Fact]
    public void MissingExtensionGlyphsSlotReadsAsEmpty()
    {
        using var temp = new TempStore();
        File.WriteAllText(temp.Paths.SettingsFile, "{ \"schemaVersion\": 2, \"skipLauncher\": false }");

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Empty(reloaded.ExtensionGlyphs);
    }

    [Fact]
    public void ExplicitNullExtensionGlyphsReadsAsEmpty()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.Paths.SettingsFile, "{ \"schemaVersion\": 2, \"extensionGlyphs\": null }");

        var reloaded = temp.Store.LoadSettings().ValueOrThrow();

        Assert.Empty(reloaded.ExtensionGlyphs);
    }

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

    [Fact]
    public void ARescheduleWhileRunningDoesNotStartASecondWriteConcurrently()
    {
        using var aStarted = new ManualResetEventSlim(false);
        using var bStarted = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(1));

        var gate = new Lock();

        var bStartedWhileARan = true;

        scheduler.Schedule("key", () =>
        {
            aStarted.Set();
            var observed = bStarted.Wait(TimeSpan.FromMilliseconds(500));
            lock (gate)
            {
                bStartedWhileARan = observed;
            }

            return null;
        });

        Assert.True(
            aStarted.Wait(TimeSpan.FromSeconds(5)),
            "A 가 시작했다는 신호를 못 받았다 — 타이머가 발화하지 않았다.");

        scheduler.Schedule("key", () =>
        {
            bStarted.Set();
            return null;
        });

        scheduler.Flush();

        bool observedDuringA;
        lock (gate)
        {
            observedDuringA = bStartedWhileARan;
        }

        Assert.False(
            observedDuringA,
            "진행 중인 쓰기가 반환하기 전에 같은 키의 둘째 쓰기가 시작됐다.");
    }

    [Fact]
    public async Task ARescheduleWhileRunningDoesNotAddASecondWrite()
    {
        using var aStarted = new ManualResetEventSlim(false);
        using var aMayLand = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(1));

        var landings = new List<string>();
        var gate = new Lock();

        scheduler.Schedule("key", () =>
        {
            aStarted.Set();
            Assert.True(aMayLand.Wait(TimeSpan.FromSeconds(5)), "A 를 놓아주는 신호를 못 받았다.");
            lock (gate)
            {
                landings.Add("A");
            }

            return null;
        });

        Assert.True(aStarted.Wait(TimeSpan.FromSeconds(5)), "A 가 시작했다는 신호를 못 받았다.");

        scheduler.Schedule("key", () =>
        {
            lock (gate)
            {
                landings.Add("B");
            }

            return null;
        });

        var flush = Task.Run(() => scheduler.Flush());

        aMayLand.Set();

        Assert.True(await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5))) == flush,
            "Flush() 가 5초 안에 반환하지 않았다.");
        await flush;

        string[] snapshot;
        lock (gate)
        {
            snapshot = [.. landings];
        }

        Assert.Equal(["A", "B"], snapshot);
    }

    [Fact]
    public async Task FlushWaitsForTheWriteThatArrivedWhileRunning()
    {
        using var aStarted = new ManualResetEventSlim(false);
        using var aMayLand = new ManualResetEventSlim(false);
        using var bLanded = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(1));

        scheduler.Schedule("key", () =>
        {
            aStarted.Set();
            Assert.True(aMayLand.Wait(TimeSpan.FromSeconds(5)), "A 를 놓아주는 신호를 못 받았다.");
            return null;
        });

        Assert.True(aStarted.Wait(TimeSpan.FromSeconds(5)), "A 가 시작했다는 신호를 못 받았다.");

        scheduler.Schedule("key", () =>
        {
            bLanded.Set();
            return null;
        });

        var flush = Task.Run(() => scheduler.Flush());

        aMayLand.Set();

        Assert.True(await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5))) == flush,
            "Flush() 가 5초 안에 반환하지 않았다.");
        await flush;

        Assert.True(bLanded.IsSet, "Flush() 가 반환했는데 재예약된 쓰기(B)가 착지하지 않았다.");
    }

    [Fact]
    public async Task SeveralReschedulesWhileRunningCollapseToOne()
    {
        using var aStarted = new ManualResetEventSlim(false);
        using var aMayLand = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(1));

        var landings = new List<string>();
        var gate = new Lock();

        scheduler.Schedule("key", () =>
        {
            aStarted.Set();
            Assert.True(aMayLand.Wait(TimeSpan.FromSeconds(5)), "A 를 놓아주는 신호를 못 받았다.");
            lock (gate)
            {
                landings.Add("A");
            }

            return null;
        });

        Assert.True(aStarted.Wait(TimeSpan.FromSeconds(5)), "A 가 시작했다는 신호를 못 받았다.");

        scheduler.Schedule("key", () =>
        {
            lock (gate)
            {
                landings.Add("B");
            }

            return null;
        });

        scheduler.Schedule("key", () =>
        {
            lock (gate)
            {
                landings.Add("C");
            }

            return null;
        });

        var flush = Task.Run(() => scheduler.Flush());

        aMayLand.Set();

        Assert.True(await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5))) == flush,
            "Flush() 가 5초 안에 반환하지 않았다.");
        await flush;

        string[] snapshot;
        lock (gate)
        {
            snapshot = [.. landings];
        }

        Assert.Equal(["A", "C"], snapshot);
    }

    [Fact]
    public void DisposingTwiceDoesNotThrow()
    {
        var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(20));
        scheduler.Schedule("key", () => null);

        scheduler.Dispose();
        scheduler.Dispose();
    }

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

    [Fact]
    public void BackgroundWriteFailureIsReported()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "쓸 수 없는 곳" };

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
