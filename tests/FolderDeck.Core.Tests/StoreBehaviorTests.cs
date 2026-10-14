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

        Assert.Equal(5, reloaded.SchemaVersion);
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
    /// 진행 중인 쓰기가 있는 키에 다시 <c>Schedule</c> 하면, 그 새 쓰기는 <b>진행 중인
    /// 쓰기가 반환한 뒤에야</b> 시작해야 한다 (D101). 고침 전에는 재예약이 자기 타이머를
    /// 단 새 <c>Pending</c> 이 되어 <b>나란히</b> 돌았고, 그래서 같은 파일을 향한 쓰기가
    /// 둘 살면서 어느 쪽이 마지막에 착지할지가 정해지지 않았다.
    /// </summary>
    /// <remarks>
    /// 교착이 없다 — A 는 스스로 500ms 뒤 풀리므로 <c>Flush()</c> 를 시험 스레드에서
    /// 불러도 안전하다. 고침 전 코드에서는 B 의 타이머(1ms)가 A 의 기다림 안에서
    /// 반드시 발화하므로 <b>결정적으로 빨갛고</b>, 고침 뒤에는 B 가 <c>Dirty</c> 로
    /// 접혀 A 가 반환해야 돌므로 A 의 기다림이 <b>반드시</b> 시간 초과해 초록이다.
    /// </remarks>
    [Fact]
    public void ARescheduleWhileRunningDoesNotStartASecondWriteConcurrently()
    {
        using var aStarted = new ManualResetEventSlim(false);
        using var bStarted = new ManualResetEventSlim(false);
        using var scheduler = new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(1));

        var gate = new Lock();

        // A 가 한 번도 안 돌면 참으로 남아 시험이 실패한다 — 안전한 쪽이 기본값이다.
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

    // ── 저장 순서 경합 고침 (D101) — 진행 중 재예약이 둘째 쓰기를 만들지 않는다 ─────

    /// <summary>
    /// A 가 진행 중일 때 같은 키로 재예약(B)하면 <c>Schedule</c> 은 새 쓰기를 만들지
    /// 않고 진행 중인 항목의 <c>Write</c> 를 갈아 끼우며 <c>Dirty</c> 만 세운다(D101 §3
    /// 결정 2). <c>Run</c> 이 A 를 마친 뒤 <c>Dirty</c> 를 보고 이어서 B 를 쓰므로,
    /// 전체 쓰기는 정확히 <b>둘</b>(A · B) 이고 마지막이 B 다 — D100 이 문 「먼저
    /// 예약된 게 나중에 착지한다」가 아예 일어날 자리가 없어진다(둘째 쓰기 자체가
    /// 없으므로 다툴 순서가 없다).
    /// </summary>
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

        // A 가 진행 중일 때 재예약한다 — 새 Pending 이 아니라 Dirty 여야 한다(D101).
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

    /// <summary>
    /// <c>Flush()</c> 가 반환한 시점에는 진행 중 재예약(B)도 이미 착지해 있다(D101) —
    /// <c>Run</c> 의 고리가 <c>Dirty</c> 를 다 갚은 뒤에야 <c>_running</c> 을 줄이고
    /// <c>_idle</c> 을 세우므로, <c>Flush()</c> 의 <c>_idle.Wait()</c> 는 이어서 쓴
    /// 것까지 포함해 기다린다.
    /// </summary>
    /// <remarks>
    /// 이 시험 안에서 <c>bLanded</c> 를 <b>미리 기다리지 않는다</b> — 그러면 검사가
    /// 무의미해진다. <c>Flush()</c> 가 반환했다는 사실 그 자체가 착지를 보장하는지를
    /// 문다.
    /// </remarks>
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

    /// <summary>
    /// A 진행 중에 B 를 예약한 뒤 다시 C 를 예약하면, <c>Schedule</c> 이 매번
    /// <c>existing.Write</c> 를 갈아 끼우므로 A 를 마친 뒤 이어서 쓰는 것은 <b>마지막
    /// (C)</b> 뿐이다 — B 는 갈아 끼워진 채 한 번도 불리지 않는다(D101).
    /// </summary>
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
