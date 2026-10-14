using FolderDeck.App.ViewModels;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Tests;

/// <summary>
/// 공유 즐겨찾기 · 대상 타일 (2026-10-14 S3). 즐겨찾기는 등록 폴더와 분리돼 있다(JH 결정) — 일반
/// 작업 관리에서 즐겨찾기를 눌러도 <b>작업 관리 파일이 한 바이트도 안 바뀌어야</b> 한다. 그것을 실물
/// 파일 바이트로 잰다.
/// </summary>
public sealed class FavoritesTests
{
    private static TileViewModel PinnedTileOf(MainWindowFixture f, string path) =>
        f.ViewModel.Tiles.Single(t => t.IsPinned
            && string.Equals(t.Panel.Entry?.Path, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task ClickingAFavoriteMovesTheTargetTileWithoutTouchingTheWorkspace()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.Favorites.AddPath(f.DocsPath);

        var file = f.Paths.WorkspaceFile(f.Workspace.Id);
        f.Store.Flush();
        var before = File.ReadAllBytes(file);
        var foldersBefore = f.Workspace.Folders.Count;

        var code = PinnedTileOf(f, f.CodePath);
        code.Panel.NotifyActivated();
        Assert.Same(code, f.ViewModel.TargetTile);
        Assert.True(code.IsTarget);
        Assert.All(f.ViewModel.Tiles.Where(t => t != code), t => Assert.False(t.IsTarget));

        await f.ViewModel.OpenFavoriteCommand.ExecuteAsync(f.ViewModel.Favorites.Rows.Single());

        Assert.Equal(f.DocsPath, code.Panel.CurrentPath, ignoreCase: true);
        Assert.Equal(f.CodePath, code.Panel.Entry!.Path);
        Assert.True(code.Panel.IsAwayFromAnchor);
        Assert.Equal(foldersBefore, f.Workspace.Folders.Count);

        f.Store.Flush();
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task WithNoTargetChosenTheFirstRotatingTileIsUsed()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.Null(f.ViewModel.TargetTile);
        Assert.Same(f.ViewModel.ResolveRotatingTile(1), f.ViewModel.EffectiveTargetTile);
    }

    /// <summary>일반 작업 관리에서 빈 칸이 대상이면 안내만 한다 — 등록 폴더를 몰래 만들지 않는다.</summary>
    [Fact]
    public async Task AnEmptyTargetTileGetsAMessageNotARegistration()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.Favorites.AddPath(f.DocsPath);
        var target = f.ViewModel.EffectiveTargetTile!;
        Assert.Null(target.Panel.Entry);
        var foldersBefore = f.Workspace.Folders.Count;

        await f.ViewModel.OpenFavoriteCommand.ExecuteAsync(f.ViewModel.Favorites.Rows.Single());

        Assert.Contains("비어 있다", f.ViewModel.Message);
        Assert.Null(target.Panel.Entry);
        Assert.Equal(foldersBefore, f.Workspace.Folders.Count);
    }

    [Fact]
    public async Task AMissingFavoriteIsGreyedAndWarnedNotRemoved()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var missing = Path.Combine(f.CodePath, "gone");
        f.ViewModel.Favorites.AddPath(missing);
        var row = f.ViewModel.Favorites.Rows.Single();

        await f.ViewModel.OpenFavoriteCommand.ExecuteAsync(row);

        Assert.False(row.IsAvailable);
        Assert.Contains("열 수 없다", f.ViewModel.Message);
        Assert.Single(f.ViewModel.Favorites.Rows);
    }

    [Fact]
    public async Task TheCurrentFolderMenuAndTheItemMenuBothAddToFavorites()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var code = PinnedTileOf(f, f.CodePath);

        code.Panel.AddCurrentFolderToFavoritesCommand.Execute(null);
        var recipe = code.Panel.Items.Single(i => i.IsDirectory && i.Name == "Recipe");
        code.Panel.AddItemToFavorites(recipe);
        var file = code.Panel.Items.Single(i => !i.IsDirectory);
        code.Panel.AddItemToFavorites(file);

        Assert.Equal(
            [f.CodePath, Path.Combine(f.CodePath, "Recipe")],
            f.ViewModel.Favorites.Rows.Select(r => r.Path));
    }

    /// <summary>다른 창(같은 파일을 보는 두 번째 묶음)은 다시 읽으면 이 창의 추가를 본다.</summary>
    [Fact]
    public async Task AnotherWindowSeesTheAddAfterReloading()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var other = new FavoritesViewModel(new FavoritesStore(f.Paths), collapsed: false, _ => { }, _ => { });
        other.Reload();
        Assert.Empty(other.Rows);

        f.ViewModel.Favorites.AddPath(f.DocsPath);
        other.Reload();

        Assert.Equal([f.DocsPath], other.Rows.Select(r => r.Path));
    }

    /// <summary>사용자가 고를 때만 등록 폴더와 이어진다.</summary>
    [Fact]
    public async Task RegisterAsFolderIsTheOnlyBridgeToTheWorkspace()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var extra = Path.Combine(Path.GetDirectoryName(f.CodePath)!, "extra");
        Directory.CreateDirectory(extra);
        f.ViewModel.Favorites.AddPath(extra);
        var before = f.Workspace.Folders.Count;

        f.ViewModel.RegisterFavoriteAsFolderCommand.Execute(f.ViewModel.Favorites.Rows.Single());

        Assert.Equal(before + 1, f.Workspace.Folders.Count);
        Assert.Single(f.ViewModel.Favorites.Rows);
    }

    [Fact]
    public async Task CollapsingIsAGlobalSetting()
    {
        var settings = new Core.Models.AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.ViewModel.Favorites.ToggleCollapsedCommand.Execute(null);

        Assert.True(settings.FavoritesCollapsed);
    }
}
