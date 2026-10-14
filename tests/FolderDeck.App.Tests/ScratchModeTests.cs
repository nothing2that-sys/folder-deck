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

        public ScratchFixture(LayoutPreset preset = LayoutPreset.Quad, IScratchShortcut? shortcut = null)
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
                isScratch: true, shortcut);
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

    // ── 단축키 ───────────────────────────────────────────────────────────────

    private sealed class RecordingShortcut(bool installed, ScratchShortcutOutcome outcome) : IScratchShortcut
    {
        public List<string> Applied { get; } = [];

        public bool IsInstalled => installed;

        public ScratchShortcutResult Apply(string hotkey)
        {
            Applied.Add(hotkey);
            return new(outcome, outcome == ScratchShortcutOutcome.Failed ? "막힘" : null);
        }
    }

    [Fact]
    public async Task ChangingTheHotkeyInSettingsRewritesTheShortcut()
    {
        var shortcut = new RecordingShortcut(installed: true, ScratchShortcutOutcome.Written);
        using var f = new ScratchFixture(shortcut: shortcut);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = new SettingsEditResult(
            f.Settings.ExtensionGlyphs, f.Settings.ShowShellIcons, f.Settings.EverythingMaxResults,
            ScratchHotkey: "Ctrl+Alt+G");

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Ctrl+Alt+G", f.Settings.ScratchHotkey);
        Assert.Equal(["Ctrl+Alt+G"], shortcut.Applied);
        Assert.Contains("Ctrl+Alt+G", f.ViewModel.Message);
        Assert.True(Assert.Single(f.SettingsEditor.Opened).ScratchShortcutInstalled);
    }

    [Fact]
    public async Task AnUnchangedHotkeyDoesNotTouchTheShortcut()
    {
        var shortcut = new RecordingShortcut(installed: true, ScratchShortcutOutcome.Written);
        using var f = new ScratchFixture(shortcut: shortcut);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = new SettingsEditResult(
            f.Settings.ExtensionGlyphs, f.Settings.ShowShellIcons, f.Settings.EverythingMaxResults,
            ScratchHotkey: f.Settings.ScratchHotkey);

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Empty(shortcut.Applied);
    }

    [Fact]
    public async Task AMissingShortcutKeepsTheValueAndSaysSo()
    {
        var shortcut = new RecordingShortcut(installed: false, ScratchShortcutOutcome.NotInstalled);
        using var f = new ScratchFixture(shortcut: shortcut);
        await f.ViewModel.InitializeAsync();
        f.SettingsEditor.Answer = new SettingsEditResult(
            f.Settings.ExtensionGlyphs, f.Settings.ShowShellIcons, f.Settings.EverythingMaxResults,
            ScratchHotkey: "");

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(ScratchHotkey.None, f.Settings.ScratchHotkey);
        Assert.Contains("바로가기가 없어", f.ViewModel.Message);
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
