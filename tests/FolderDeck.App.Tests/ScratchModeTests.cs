using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;
using static FolderDeck.App.Tests.StaWpfTestSupport;

namespace FolderDeck.App.Tests;

/// <summary>
/// 1회용 창 (2026-10-14 S4). 실물 <see cref="WorkspaceStore"/> 를 <see cref="ScratchWorkspaceStore"/> 로
/// 감싸서 세운다 — 래퍼를 흉내 낸 가짜로 재면 실물이 새도 초록이 된다.
/// </summary>
public sealed class ScratchModeTests
{
    /// <summary>1회용 뷰모델 한 벌. 폴더 둘(a · b)을 임시 루트에 만든다.</summary>
    private sealed class ScratchFixture : IDisposable
    {
        private readonly string _root;

        public ScratchFixture(LayoutPreset preset = LayoutPreset.Quad, IHotkeyRouting? routing = null)
        {
            _root = Path.Combine(Path.GetTempPath(), "FolderDeck.ScratchTests", Guid.NewGuid().ToString("N"));
            APath = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
            BPath = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
            File.WriteAllText(Path.Combine(APath, "a.txt"), "a");

            Paths = new FolderDeckPaths(Path.Combine(_root, "appdata"));
            Paths.EnsureCreated();
            Inner = new WorkspaceStore(Paths, new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(10)));
            Settings = new AppSettings();
            SettingsEditor = new FakeSettingsEditor();

            Workspace = new Workspace { Title = "1회용", Folders = [], Tiles = LayoutPresets.Build(preset) };
            ViewModel = new MainViewModel(
                Workspace, new ScratchWorkspaceStore(Inner, Workspace.Id), new FolderEnumerator(),
                new FakeShellLauncher(), new FakeClipboardService(), new FileOperationEngine(new FakeRecycleBin()),
                new FakeUserPrompt(), new FakeMacroEditor(), new FakeFolderEditor(), new FakeSelfLauncher(),
                Settings, settingsWritable: true, SettingsEditor, new FavoritesStore(Paths),
                isScratch: true, routing);
        }

        public string APath { get; }

        public string BPath { get; }

        public FolderDeckPaths Paths { get; }

        public WorkspaceStore Inner { get; }

        public AppSettings Settings { get; }

        public FakeSettingsEditor SettingsEditor { get; }

        public Workspace Workspace { get; }

        public MainViewModel ViewModel { get; }

        public int WorkspaceFileCount() => Directory.GetFiles(Paths.WorkspacesDir).Length;

        public void Dispose()
        {
            Inner.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // 정리 실패는 무시.
            }
        }
    }

    private static LayoutPresetChoice Choice(MainViewModel vm, LayoutPreset preset) =>
        vm.ScratchLayoutChoices.Single(c => c.Preset == preset);

    // ── 명령줄 ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheScratchSwitchIsRead()
    {
        var options = CommandLineOptions.Parse(["--scratch"]);

        Assert.True(options.Scratch);
        Assert.Null(options.Error);
        Assert.Null(options.WorkspaceId);
    }

    /// <summary>둘 다 주면 무엇을 열지 모른다 — 오류를 내고 둘 다 버려 선택 창으로 떨어진다.</summary>
    [Fact]
    public void ScratchWithAWorkspaceIsAnErrorAndOpensNeither()
    {
        var options = CommandLineOptions.Parse(["--scratch", "--workspace", Guid.NewGuid().ToString()]);

        Assert.False(options.Scratch);
        Assert.Null(options.WorkspaceId);
        Assert.NotNull(options.Error);
    }

    // ── 저장 차단 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 이 회차의 핵심 불변 — 1회용 창에서 폴더를 등록하고, 배치를 바꾸고, 빈 칸에 즐겨찾기를 띄워도
    /// <c>workspaces\</c> 아래에 파일이 하나도 생기지 않는다. 설정 저장은 여전히 된다.
    /// </summary>
    [Fact]
    public async Task NothingOfTheScratchWorkspaceReachesTheDisk()
    {
        using var f = new ScratchFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.AddFolders([f.APath]);
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(f.ViewModel.Rows.Single());
        await f.ViewModel.ApplyScratchLayoutCommand.ExecuteAsync(Choice(f.ViewModel, LayoutPreset.Single));
        f.ViewModel.Favorites.AddPath(f.BPath);
        f.ViewModel.Favorites.ToggleCollapsedCommand.Execute(null);
        f.Inner.Flush();

        Assert.Equal(0, f.WorkspaceFileCount());
        Assert.True(File.Exists(f.Paths.SettingsFile));
        Assert.True(f.Inner.LoadSettings().Value!.FavoritesCollapsed);
    }

    [Fact]
    public void TheWrapperStillSavesOtherWorkspaces()
    {
        using var f = new ScratchFixture();
        var other = new Workspace { Title = "다른 것" };
        var wrapper = new ScratchWorkspaceStore(f.Inner, f.Workspace.Id);

        Assert.Null(wrapper.SaveWorkspace(other));
        Assert.Null(wrapper.SaveWorkspace(f.Workspace));

        Assert.Equal([f.Paths.WorkspaceFile(other.Id)], Directory.GetFiles(f.Paths.WorkspacesDir));
    }

    [Fact]
    public void TheTitleSaysScratch()
    {
        using var f = new ScratchFixture();

        Assert.Contains("1회용", f.ViewModel.WindowTitle);
    }

    // ── 배치 프리셋 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchingFromQuadToSingleKeepsTheFirstFolderAndTheList()
    {
        using var f = new ScratchFixture(LayoutPreset.Quad);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.AddFolders([f.APath, f.BPath]);
        var a = f.ViewModel.Rows.Single(r => r.Entry.Path == f.APath);
        var b = f.ViewModel.Rows.Single(r => r.Entry.Path == f.BPath);
        b.Entry.RotationSlot = 2;
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(a);
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(b);
        Assert.Equal(4, f.ViewModel.Tiles.Count);

        await f.ViewModel.ApplyScratchLayoutCommand.ExecuteAsync(Choice(f.ViewModel, LayoutPreset.Single));

        var only = Assert.Single(f.ViewModel.Tiles);
        Assert.Same(a.Entry, only.Panel.Entry);
        Assert.Equal(2, f.Workspace.Folders.Count);
        Assert.Contains("칸에서 빠졌다", f.ViewModel.Message);
    }

    [Fact]
    public async Task SwitchingToMoreTilesRefillsInReadingOrder()
    {
        using var f = new ScratchFixture(LayoutPreset.SideBySide);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.AddFolders([f.APath, f.BPath]);
        var a = f.ViewModel.Rows.Single(r => r.Entry.Path == f.APath);
        var b = f.ViewModel.Rows.Single(r => r.Entry.Path == f.BPath);
        b.Entry.RotationSlot = 2;
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(a);
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(b);

        await f.ViewModel.ApplyScratchLayoutCommand.ExecuteAsync(Choice(f.ViewModel, LayoutPreset.Quad));

        var ordered = f.ViewModel.Tiles.OrderBy(t => t.RotationIndex).ToList();
        Assert.Same(a.Entry, ordered[0].Panel.Entry);
        Assert.Same(b.Entry, ordered[1].Panel.Entry);
        Assert.Null(ordered[2].Panel.Entry);
        Assert.Null(ordered[3].Panel.Entry);
    }

    /// <summary>일반 작업 관리에서는 배치 프리셋이 아무 일도 하지 않는다(이번 회차 범위 밖).</summary>
    [Fact]
    public async Task APresetDoesNothingOutsideScratch()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var before = f.ViewModel.Tiles.ToList();

        await f.ViewModel.ApplyScratchLayoutCommand.ExecuteAsync(Choice(f.ViewModel, LayoutPreset.Single));

        Assert.Equal(before, f.ViewModel.Tiles);
        Assert.False(f.ViewModel.IsScratch);
    }

    // ── 빈 칸에 즐겨찾기 ─────────────────────────────────────────────────────

    [Fact]
    public async Task AFavoriteOnAnEmptyScratchTileIsRegisteredInMemoryAndShownThere()
    {
        using var f = new ScratchFixture(LayoutPreset.SideBySide);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.Favorites.AddPath(f.BPath);
        var right = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);
        right.Panel.NotifyActivated();

        await f.ViewModel.OpenFavoriteCommand.ExecuteAsync(f.ViewModel.Favorites.Rows.Single());

        Assert.Equal(f.BPath, right.Panel.Entry!.Path, ignoreCase: true);
        Assert.Null(f.ViewModel.Tiles.Single(t => t.RotationIndex == 1).Panel.Entry);
        Assert.Single(f.Workspace.Folders);

        f.Inner.Flush();
        Assert.Equal(0, f.WorkspaceFileCount());
    }

    // ── 단축키 받는 쪽 (S4 → 2026-10-14 2차에서 다시 씀) ──────────────────────────
    //
    // 앞 회차에는 설정 저장이 바로가기(.lnk)만 고쳤다. 2차에서 JH 가 「상주 / 바로가기」를 사용자가 고르게 정해
    // 설정 저장은 HotkeyRouting 을 부른다 — 아래 셋은 그에 맞게 다시 쓴 것이다(지시서가 기대값이 바뀐다고 적은 자리).

    /// <summary>뷰모델이 무엇을 맞추라고 했는지 적는 가짜. 실제 Run 키·바로가기·프로세스에 손대지 않는다.</summary>
    private sealed class RecordingRouting(bool shortcutInstalled, bool agentRunning) : IHotkeyRouting, IAgentControl
    {
        public List<(string Hotkey, bool UseAgent)> Applied { get; } = [];

        /// <summary>각 Apply 때 「상주가 아직 키를 쥐고 있다」고 넘겼는지.</summary>
        public List<bool> AgentHeldKey { get; } = [];

        public int Starts { get; private set; }

        public IAgentControl Agent => this;

        public bool ShortcutInstalled => shortcutInstalled;

        public bool IsRunning => agentRunning;

        public string? Apply(AppSettings settings, bool agentHoldsKey = false)
        {
            Applied.Add((settings.ScratchHotkey, settings.UseResidentAgent));
            AgentHeldKey.Add(agentHoldsKey);
            return null;
        }

        public string? Start()
        {
            Starts++;
            return null;
        }

        public string? HandOverToShortcut(AppSettings settings) => null;

        public bool TrySignalScratch() => false;

        public bool TrySignalExit() => false;
    }

    private static SettingsEditResult Answer(AppSettings s, string? hotkey = null, bool? useAgent = null) =>
        new(s.ExtensionGlyphs, s.ShowShellIcons, s.EverythingMaxResults, ScratchHotkey: hotkey, UseResidentAgent: useAgent);

    [Fact]
    public async Task ChangingTheHotkeyRoutesItAndStartsAMissingAgent()
    {
        var routing = new RecordingRouting(shortcutInstalled: true, agentRunning: false);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, hotkey: "Ctrl+Alt+G");

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Ctrl+Alt+G", f.Settings.ScratchHotkey);
        Assert.Equal([("Ctrl+Alt+G", true)], routing.Applied);
        Assert.Equal(1, routing.Starts);
        Assert.Contains("상주 프로그램", f.ViewModel.Message);
        var request = Assert.Single(f.SettingsEditor.Opened);
        Assert.True(request.UseResidentAgent);
        Assert.False(request.AgentRunning);
    }

    /// <summary>새 상주는 시작할 때 settings.json 을 읽는다 — 띄우기 전에 디바운스를 기다리지 않고 써 있어야 한다.</summary>
    [Fact]
    public async Task TheSettingsFileIsWrittenBeforeTheAgentIsStarted()
    {
        var routing = new RecordingRouting(shortcutInstalled: true, agentRunning: false);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, hotkey: "Ctrl+Alt+G");

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Ctrl+Alt+G", f.Inner.LoadSettings().Value!.ScratchHotkey);
    }

    [Fact]
    public async Task ARunningAgentIsNotStartedAgain()
    {
        var routing = new RecordingRouting(shortcutInstalled: true, agentRunning: true);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, hotkey: "Ctrl+Alt+G");

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0, routing.Starts);
    }

    [Fact]
    public async Task AnUnchangedHotkeyAndModeDoNotRoute()
    {
        var routing = new RecordingRouting(shortcutInstalled: true, agentRunning: false);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, hotkey: f.Settings.ScratchHotkey, useAgent: f.Settings.UseResidentAgent);

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Empty(routing.Applied);
        Assert.Equal(0, routing.Starts);
    }

    [Fact]
    public async Task TurningTheAgentOffRoutesToTheShortcutWithoutStartingAnything()
    {
        var routing = new RecordingRouting(shortcutInstalled: true, agentRunning: true);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, useAgent: false);

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.False(f.Settings.UseResidentAgent);
        Assert.Equal([(f.Settings.ScratchHotkey, false)], routing.Applied);
        Assert.Equal(0, routing.Starts);

        // 상주가 떠 있으니 아직 키를 쥐고 있다 — 바로가기 키는 상주가 물러나며 쓴다(JH 손확인 버그).
        Assert.Equal([true], routing.AgentHeldKey);
        Assert.Contains("바로가기", f.ViewModel.Message);
        Assert.False(f.Inner.LoadSettings().Value!.UseResidentAgent);
    }

    [Fact]
    public async Task AMissingShortcutWithTheAgentOffKeepsTheValueAndSaysSo()
    {
        var routing = new RecordingRouting(shortcutInstalled: false, agentRunning: false);
        using var f = new ScratchFixture(routing: routing);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = Answer(f.Settings, hotkey: "", useAgent: false);

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(ScratchHotkey.None, f.Settings.ScratchHotkey);
        Assert.Contains("바로가기가 없어", f.ViewModel.Message);
    }

    // ── HotkeyRouting 실물 규칙 (가짜 바로가기 · 가짜 Run 키) ─────────────────────

    private sealed class FakeShortcut(bool installed) : IScratchShortcut
    {
        public List<string> Applied { get; } = [];

        public bool IsInstalled => installed;

        public ScratchShortcutResult Apply(string hotkey)
        {
            Applied.Add(hotkey);
            return new(installed ? ScratchShortcutOutcome.Written : ScratchShortcutOutcome.NotInstalled);
        }
    }

    private sealed class FakeRunKey : IRunKey
    {
        public string? Value { get; set; }

        public int Writes { get; private set; }

        public string? Read() => Value;

        public void Write(string command)
        {
            Value = command;
            Writes++;
        }

        public void Delete() => Value = null;
    }

    /// <summary>상주를 쓰면 바로가기 키는 지우고 Run 키를 쓴다 — 둘 다 켜지면 창이 두 번 뜬다.</summary>
    [Fact]
    public void WithTheAgentTheShortcutKeyIsClearedAndTheRunKeyIsWritten()
    {
        var shortcut = new FakeShortcut(installed: true);
        var run = new FakeRunKey();
        var routing = new HotkeyRouting(shortcut, run, NullHotkeyRouting.Instance, @"C:\Program Files\FD\FolderDeck.exe");

        Assert.Null(routing.Apply(new AppSettings { UseResidentAgent = true, ScratchHotkey = "Ctrl+Alt+G" }));
        Assert.Null(routing.Apply(new AppSettings { UseResidentAgent = true, ScratchHotkey = "Ctrl+Alt+G" }));

        Assert.Equal([ScratchHotkey.None, ScratchHotkey.None], shortcut.Applied);
        Assert.Equal("\"C:\\Program Files\\FD\\FolderDeck.exe\" --agent", run.Value);
        Assert.Equal(1, run.Writes);
    }

    /// <summary>
    /// 「끔」인데 상주가 아직 키를 쥐고 있으면 바로가기에 키를 쓰지 않는다 — 탐색기가 등록하려다 상주에 막히면 다시 시도하지
    /// 않아 단축키가 죽는다(JH 손확인). Run 키는 그래도 지운다.
    /// </summary>
    [Fact]
    public void WhileTheAgentStillHoldsTheKeyTheShortcutStaysEmpty()
    {
        var shortcut = new FakeShortcut(installed: true);
        var run = new FakeRunKey { Value = "\"x\" --agent" };
        var routing = new HotkeyRouting(shortcut, run, NullHotkeyRouting.Instance, @"C:\FD\FolderDeck.exe");

        Assert.Null(routing.Apply(new AppSettings { UseResidentAgent = false, ScratchHotkey = "Ctrl+Alt+G" }, agentHoldsKey: true));

        Assert.Equal([ScratchHotkey.None], shortcut.Applied);
        Assert.Null(run.Value);

        // 상주가 키를 놓은 뒤 다시 부르면 그때 쓴다.
        Assert.Null(routing.Apply(new AppSettings { UseResidentAgent = false, ScratchHotkey = "Ctrl+Alt+G" }));
        Assert.Equal([ScratchHotkey.None, "Ctrl+Alt+G"], shortcut.Applied);
    }

    /// <summary>
    /// 상주가 끝날 때 단축키를 바로가기에 넘긴다(JH 결정) — 키만 쓰고 Run 키는 그대로(설정은 여전히 「켬」). 다음 시작이
    /// <see cref="HotkeyRouting.Apply"/> 로 다시 지운다.
    /// </summary>
    [Fact]
    public void HandingOverWritesTheShortcutKeyAndKeepsTheRunKey()
    {
        var shortcut = new FakeShortcut(installed: true);
        var run = new FakeRunKey { Value = "\"x\" --agent" };
        var routing = new HotkeyRouting(shortcut, run, NullHotkeyRouting.Instance, "x");
        var settings = new AppSettings { UseResidentAgent = true, ScratchHotkey = "Ctrl+Alt+G" };

        Assert.Null(routing.HandOverToShortcut(settings));
        Assert.Equal(["Ctrl+Alt+G"], shortcut.Applied);
        Assert.Equal("\"x\" --agent", run.Value);

        Assert.Null(routing.Apply(settings));
        Assert.Equal(["Ctrl+Alt+G", ScratchHotkey.None], shortcut.Applied);
    }

    /// <summary>트레이 「상주 끝내기」 뒤 앱을 다시 실행하면 상주도 다시 떠야 한다(JH 손확인 버그).</summary>
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public void ALaunchStartsTheAgentOnlyWhenItIsWantedWritableAndMissing(
        bool useAgent, bool writable, bool running, bool expected) =>
        Assert.Equal(expected, HotkeyRoutingRules.ShouldStartAgent(useAgent, writable, running));

    [Fact]
    public void WithoutTheAgentTheShortcutGetsTheKeyAndTheRunKeyIsRemoved()
    {
        var shortcut = new FakeShortcut(installed: true);
        var run = new FakeRunKey { Value = "\"x\" --agent" };
        var routing = new HotkeyRouting(shortcut, run, NullHotkeyRouting.Instance, @"C:\FD\FolderDeck.exe");

        Assert.Null(routing.Apply(new AppSettings { UseResidentAgent = false, ScratchHotkey = "Ctrl+Alt+G" }));

        Assert.Equal(["Ctrl+Alt+G"], shortcut.Applied);
        Assert.Null(run.Value);
    }

    // ── 상주 단축키 등록 ─────────────────────────────────────────────────────────

    private sealed class FakeRegistrar(string? failure = null) : IHotkeyRegistrar
    {
        public List<string> Calls { get; } = [];

        public string? Register(uint modifiers, uint virtualKey)
        {
            Calls.Add($"register {modifiers:X} {virtualKey:X}");
            return failure;
        }

        public void Unregister() => Calls.Add("unregister");
    }

    [Fact]
    public void TheBinderRegistersOnceAndReRegistersOnlyOnChange()
    {
        var registrar = new FakeRegistrar();
        var binder = new AgentHotkeyBinder(registrar);

        Assert.Null(binder.Update("Ctrl+Alt+F"));
        Assert.Null(binder.Update("Ctrl+Alt+F"));
        Assert.Null(binder.Update("Ctrl+Alt+Shift+G"));
        Assert.Null(binder.Update(ScratchHotkey.None));

        Assert.Equal(
            [
                "unregister", "register 4003 46",
                "unregister", "register 4007 47",
                "unregister",
            ],
            registrar.Calls);
        Assert.Null(binder.Registered);
    }

    [Fact]
    public void AFailedRegistrationLeavesNothingRegisteredAndSaysWhy()
    {
        var binder = new AgentHotkeyBinder(new FakeRegistrar("다른 프로그램이 이미 그 단축키를 쓰고 있다."));

        Assert.NotNull(binder.Update("Ctrl+Alt+F"));
        Assert.Null(binder.Registered);
    }

    /// <summary>
    /// 실물 <c>RegisterHotKey</c> — 등록 · 숨은 창으로 <c>WM_HOTKEY</c> 받기 · 해제. 이 PC 에서 쓰일 일이 거의 없는
    /// 조합을 쓴다. 그래도 이미 누가 쓰고 있으면 실패가 정상이라, 그때는 그 사실만 확인하고 끝낸다.
    /// </summary>
    [Fact]
    public void TheRealHotkeyRegistersReceivesAndUnregisters()
    {
        RunOnSta(() =>
        {
            var pressed = 0;
            using var hotkey = new GlobalHotkey(() => pressed++);
            var registration = ScratchHotkey.ToRegistration("Ctrl+Alt+Shift+F11")!.Value;

            var failure = hotkey.Register(registration.Modifiers, registration.VirtualKey);
            if (failure is not null)
            {
                Assert.Contains("이미", failure);
                return;
            }

            hotkey.SimulatePress();
            Assert.Equal(1, pressed);

            hotkey.Unregister();
            var again = hotkey.Register(registration.Modifiers, registration.VirtualKey);
            Assert.Null(again);
            hotkey.Unregister();
        });
    }

    // ── 시작 메뉴 위임 · 명령줄 ─────────────────────────────────────────────────

    private sealed class FakeAgent(bool running, bool delivers) : IAgentControl
    {
        public int Signals { get; private set; }

        public bool IsRunning => running;

        public string? Start() => null;

        public bool TrySignalScratch()
        {
            Signals++;
            return delivers;
        }

        public bool TrySignalExit() => delivers;
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void TheStartMenuDelegatesOnlyWhenTheAgentIsUsedAliveAndReached(
        bool useAgent, bool running, bool delivers, bool expected)
    {
        var agent = new FakeAgent(running, delivers);

        Assert.Equal(expected, ScratchLaunch.DelegatedToAgent(useAgent, agent));
        if (!useAgent || !running)
        {
            Assert.Equal(0, agent.Signals);
        }
    }

    [Theory]
    [InlineData("--agent", true, false)]
    [InlineData("--agent-exit", false, true)]
    public void TheAgentSwitchesAreRead(string arg, bool agent, bool exit)
    {
        var options = CommandLineOptions.Parse([arg]);

        Assert.Equal(agent, options.Agent);
        Assert.Equal(exit, options.AgentExit);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("--agent", "--scratch")]
    [InlineData("--agent", "--agent-exit")]
    [InlineData("--launcher", "--agent-exit")]
    public void TheAgentSwitchesMustStandAlone(string a, string b)
    {
        var options = CommandLineOptions.Parse([a, b]);

        Assert.False(options.Agent);
        Assert.False(options.AgentExit);
        Assert.NotNull(options.Error);
    }

    // ── 상주 안 1회용 창 ─────────────────────────────────────────────────────────

    /// <summary>상주는 한 프로세스에 1회용 창을 여럿 연다 — 세고, 닫고, 디스크에는 아무것도 안 남긴다.</summary>
    [Fact]
    public void TheHostOpensSeveralScratchWindowsAndClosesThemAll()
    {
        RunOnSta(() =>
        {
            using var f = new ScratchFixture();
            var host = new ScratchWindowHost(
                f.Inner,
                () => LayoutPreset.SideBySide,
                (workspace, store) => new MainWindow(
                    new MainViewModel(
                        workspace, store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
                        new FileOperationEngine(new FakeRecycleBin()), new FakeUserPrompt(), new FakeMacroEditor(),
                        new FakeFolderEditor(), new FakeSelfLauncher(), f.Settings, settingsWritable: true,
                        f.SettingsEditor, new FavoritesStore(f.Paths), isScratch: true),
                    new FakeFolderPicker()));

            var first = host.Open();
            var second = host.Open();

            Assert.Equal(2, host.OpenWindows.Count);
            Assert.NotSame(first, second);
            Assert.Equal(2, ((MainWindow)first).ViewModel.Tiles.Count);

            first.Close();
            Assert.Single(host.OpenWindows);

            host.CloseAll();
            Assert.Empty(host.OpenWindows);

            f.Inner.Flush();
            Assert.Equal(0, f.WorkspaceFileCount());
        });
    }

    private static ScratchWindowHost HostFor(ScratchFixture f) => new(
        f.Inner,
        () => LayoutPreset.SideBySide,
        (workspace, store) => new MainWindow(
            new MainViewModel(
                workspace, store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
                new FileOperationEngine(new FakeRecycleBin()), new FakeUserPrompt(), new FakeMacroEditor(),
                new FakeFolderEditor(), new FakeSelfLauncher(), f.Settings, settingsWritable: true,
                f.SettingsEditor, new FavoritesStore(f.Paths), isScratch: true),
            new FakeFolderPicker()));

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 10000)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>데우기 창은 세지 않고, 첫 렌더 뒤 스스로 닫히고, 「마지막 창 닫힘」을 쏘지 않는다.</summary>
    [Fact]
    public void TheWarmUpWindowClosesItselfUncounted()
    {
        RunOnSta(() =>
        {
            using var f = new ScratchFixture();
            var host = HostFor(f);
            var lastClosed = 0;
            host.LastWindowClosed += (_, _) => lastClosed++;
            var done = false;

            host.WarmUp(() => done = true);
            Assert.Empty(host.OpenWindows);
            PumpUntil(() => done);

            Assert.True(done);
            Assert.Empty(host.OpenWindows);
            Assert.Equal(0, lastClosed);
        });
    }

    /// <summary>「상주로 받기」를 끄면 상주는 이 알림을 기다렸다 끝난다 — 마지막 창이 닫힐 때 한 번만 와야 한다.</summary>
    [Fact]
    public void LastWindowClosedFiresOnlyWhenTheLastOneCloses()
    {
        RunOnSta(() =>
        {
            using var f = new ScratchFixture();
            var host = HostFor(f);
            var lastClosed = 0;
            host.LastWindowClosed += (_, _) => lastClosed++;

            var first = host.Open();
            var second = host.Open();
            first.Close();
            Assert.Equal(0, lastClosed);

            second.Close();
            Assert.Equal(1, lastClosed);
        });
    }

    [Fact]
    public void TheHotkeyFieldRefusesAComboTheShortcutCannotHold()
    {
        var vm = new ScratchSettingsViewModel(LayoutPreset.SideBySide, "Ctrl+Alt+F", shortcutInstalled: true);

        vm.Capture(ctrl: true, alt: false, shift: false, "F");
        Assert.Equal("Ctrl+Alt+F", vm.HotkeyText);
        Assert.NotNull(vm.HotkeyMessage);

        vm.Capture(ctrl: true, alt: true, shift: true, "G");
        Assert.Equal("Ctrl+Alt+Shift+G", vm.HotkeyText);
        Assert.Null(vm.HotkeyMessage);

        vm.ClearHotkeyCommand.Execute(null);
        Assert.Equal("없음", vm.HotkeyDisplay);
    }

    // ── 실물 .lnk ────────────────────────────────────────────────────────────

    /// <summary>임시 폴더에 실물 바로가기를 만들어 COM 으로 쓰고 다시 읽는다.</summary>
    [Fact]
    public void TheShortcutHotkeyIsWrittenToARealLnkAndReadBack()
    {
        var dir = Directory.CreateTempSubdirectory("FolderDeck.LnkTests").FullName;
        try
        {
            var lnk = Path.Combine(dir, "Folder Deck 1회용.lnk");
            var target = Path.Combine(dir, "target.txt");
            File.WriteAllText(target, "x");
            ScratchShortcut.CreateForTest(lnk, target);
            var shortcut = new ScratchShortcut(lnk);

            Assert.Equal(ScratchShortcutOutcome.Written, shortcut.Apply("Ctrl+Alt+G").Outcome);
            Assert.Equal(ScratchHotkey.ToShortcutValue("Ctrl+Alt+G"), shortcut.ReadHotkey());

            Assert.Equal(ScratchShortcutOutcome.Unchanged, shortcut.Apply("Ctrl+Alt+G").Outcome);

            Assert.Equal(ScratchShortcutOutcome.Written, shortcut.Apply(ScratchHotkey.None).Outcome);
            Assert.Equal((ushort)0, shortcut.ReadHotkey());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AMissingLnkIsNotCreated()
    {
        var dir = Directory.CreateTempSubdirectory("FolderDeck.LnkTests").FullName;
        try
        {
            var lnk = Path.Combine(dir, "none.lnk");

            Assert.Equal(ScratchShortcutOutcome.NotInstalled, new ScratchShortcut(lnk).Apply("Ctrl+Alt+G").Outcome);
            Assert.False(File.Exists(lnk));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── 앵커 빼기 (2026-10-14 2차 A1) ────────────────────────────────────────

    private static async Task<TileViewModel> ShowInFirstTileAsync(ScratchFixture f)
    {
        f.ViewModel.AddFolders([f.APath]);
        await f.ViewModel.ShowFolderCommand.ExecuteAsync(f.ViewModel.Rows.Single(r => r.Entry.Path == f.APath));
        return f.ViewModel.Tiles.Single(t => t.Panel.Entry is not null);
    }

    [Fact]
    public async Task GoingIntoASubfolderShowsNoAnchorWordsInScratch()
    {
        using var f = new ScratchFixture(LayoutPreset.SideBySide);
        await f.ViewModel.InitializeAsync();
        var sub = Directory.CreateDirectory(Path.Combine(f.APath, "sub")).FullName;
        var tile = await ShowInFirstTileAsync(f);

        await tile.Panel.NavigateToAsync(sub);

        Assert.True(tile.Panel.IsAwayFromAnchor);
        Assert.False(tile.Panel.ShowsAwayFromAnchor);
        Assert.False(tile.Panel.HasStatusLine);
        Assert.False(tile.Panel.ShowsAnchorActions);
        Assert.Equal(Path.GetPathRoot(sub), tile.Panel.Breadcrumbs[0].Path);
        Assert.DoesNotContain(tile.Panel.Breadcrumbs, b => b.IsAnchor);
        Assert.DoesNotContain("앵커", tile.Panel.LocationHint);
    }

    /// <summary>같은 동작이 일반 작업 관리에서는 예전 그대로다 — 앵커로 접은 브레드크럼, 「앵커 밖」 표시.</summary>
    [Fact]
    public async Task ANormalWorkspaceStillShowsTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var code = f.ViewModel.Tiles.Single(t => t.IsPinned
            && string.Equals(t.Panel.Entry?.Path, f.CodePath, StringComparison.OrdinalIgnoreCase));

        await code.Panel.NavigateToAsync(Path.Combine(f.CodePath, "Recipe"));

        Assert.True(code.Panel.ShowsAwayFromAnchor);
        Assert.True(code.Panel.HasStatusLine);
        Assert.True(code.Panel.ShowsAnchorActions);
        Assert.True(code.Panel.Breadcrumbs[0].IsAnchor);
        Assert.Contains("앵커로 복귀", f.ViewModel.FolderListHint);
    }

    [Fact]
    public async Task TheHomeKeyDoesNothingInScratchButStillWorksInANormalWorkspace()
    {
        using var s = new ScratchFixture(LayoutPreset.SideBySide);
        await s.ViewModel.InitializeAsync();
        var sub = Directory.CreateDirectory(Path.Combine(s.APath, "sub")).FullName;
        var scratchTile = await ShowInFirstTileAsync(s);
        await scratchTile.Panel.NavigateToAsync(sub);

        await scratchTile.Panel.GoToAnchorFromUserCommand.ExecuteAsync(null);

        Assert.Equal(sub, scratchTile.Panel.CurrentPath, ignoreCase: true);

        using var n = new MainWindowFixture();
        await n.ViewModel.InitializeAsync();
        var code = n.ViewModel.Tiles.Single(t => t.IsPinned
            && string.Equals(t.Panel.Entry?.Path, n.CodePath, StringComparison.OrdinalIgnoreCase));
        await code.Panel.NavigateToAsync(Path.Combine(n.CodePath, "Recipe"));

        await code.Panel.GoToAnchorFromUserCommand.ExecuteAsync(null);

        Assert.Equal(n.CodePath, code.Panel.CurrentPath, ignoreCase: true);
    }

    /// <summary>1회용 창에서는 폴더가 떠 있는 칸이라도 즐겨찾기가 그 칸의 폴더를 바꾼다(임시 이동이 아니다).</summary>
    [Fact]
    public async Task AFavoriteReplacesTheFolderOfAnOccupiedScratchTile()
    {
        using var f = new ScratchFixture(LayoutPreset.SideBySide);
        await f.ViewModel.InitializeAsync();
        var tile = await ShowInFirstTileAsync(f);
        tile.Panel.NotifyActivated();
        f.ViewModel.Favorites.AddPath(f.BPath);

        await f.ViewModel.OpenFavoriteCommand.ExecuteAsync(f.ViewModel.Favorites.Rows.Single());

        Assert.Equal(f.BPath, tile.Panel.Entry!.Path, ignoreCase: true);
        Assert.False(tile.Panel.IsAwayFromAnchor);
    }

    [Fact]
    public void TheShellMenuCanLeaveOutSetAnchor()
    {
        Assert.Contains(ShellMenuPolicy.AppItems(isDirectory: true), i => i.Command == ShellMenuAppCommand.SetAnchor);
        Assert.DoesNotContain(
            ShellMenuPolicy.AppItems(isDirectory: true, includeSetAnchor: false),
            i => i.Command == ShellMenuAppCommand.SetAnchor);
    }

    [Fact]
    public void TheFolderListHintDropsTheAnchorWordInScratch()
    {
        using var f = new ScratchFixture();

        Assert.DoesNotContain("앵커", f.ViewModel.FolderListHint);
    }

    // ── 창 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheScratchWindowConstructsAndShowsTheLayoutMenu()
    {
        RunOnSta(() =>
        {
            using var f = new ScratchFixture();
            var window = new MainWindow(f.ViewModel, new FakeFolderPicker());
            try
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var item = (System.Windows.Controls.MenuItem)window.FindName("ScratchLayoutMenuItem");
                Assert.Equal(System.Windows.Visibility.Visible, item.Visibility);
                Assert.Equal(4, item.Items.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ANormalWindowHidesTheLayoutMenu()
    {
        RunOnSta(() =>
        {
            using var f = new MainWindowFixture();
            var window = new MainWindow(f.ViewModel, new FakeFolderPicker());
            try
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var item = (System.Windows.Controls.MenuItem)window.FindName("ScratchLayoutMenuItem");
                Assert.Equal(System.Windows.Visibility.Collapsed, item.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
