using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class CardFolderLineTests
{
    private static void SeedMixed(LauncherFixture f, string title, int pinned, int rotating)
    {
        var folders = new List<FolderEntry>();
        for (var i = 0; i < pinned; i++)
        {
            folders.Add(new FolderEntry
            {
                Path = f.MakeRealFolder($"{title}-pin{i}"), DisplayName = $"pin{i}", Pinned = true,
            });
        }

        for (var i = 0; i < rotating; i++)
        {
            folders.Add(new FolderEntry
            {
                Path = f.MakeRealFolder($"{title}-rot{i}"), DisplayName = $"rot{i}", Pinned = false,
            });
        }

        Assert.Null(f.Store.SaveWorkspace(
            new Workspace { Title = title, LastUsed = DateTimeOffset.Now, Folders = folders }));
        f.ViewModel.Refresh();
    }

    [Fact]
    public void RotatingFoldersAreNamedNotCounted()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "구성", pinned: 2, rotating: 3);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal("pin0 · pin1", card.PinnedNamesText);
        Assert.Equal("rot0 · rot1 · rot2", card.RotatingNamesText);
        Assert.True(card.HasRotatingNames);
    }

    [Fact]
    public void TheCountLeadsTheLine()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "구성", pinned: 2, rotating: 3);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal("폴더 5 · pin0 · pin1 · rot0 · rot1 · rot2", card.FolderNamesText);
        Assert.StartsWith(card.FolderCountText, card.FolderNamesText);
    }

    [Fact]
    public void APieceThatIsEmptyBringsNoSeparator()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "전부 순환", pinned: 0, rotating: 2);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.False(card.HasPinnedNames);
        Assert.Equal(string.Empty, card.PinnedNamesRun);
        Assert.Equal("폴더 2 · rot0 · rot1", card.FolderNamesText);
    }

    [Fact]
    public void EverythingPinnedLeavesTheRotatingPieceEmpty()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "전부 상시", pinned: 2, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.False(card.HasRotatingNames);
        Assert.Equal(string.Empty, card.RotatingNamesRun);
        Assert.Equal("폴더 2 · pin0 · pin1", card.FolderNamesText);
    }

    [Fact]
    public void RotatingNamesAreCappedTheSameWayPinnedOnesAre()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "많음", pinned: 0, rotating: 7);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal("rot0 · rot1 · rot2 · rot3 +3", card.RotatingNamesText);
    }

    [Fact]
    public void AWorkspaceWithNoFoldersHasAnEmptyLine()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "빈 것", pinned: 0, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.True(card.HasNoFolders);
        Assert.False(card.HasPinnedNames);
        Assert.False(card.HasRotatingNames);
    }

    [Fact]
    public void ARotatingNameFallsBackToTheFolderLeaf()
    {
        using var f = new LauncherFixture();
        var path = f.MakeRealFolder("rot-leaf");
        Assert.Null(f.Store.SaveWorkspace(new Workspace
        {
            Title = "이름 없음",
            Folders = [new FolderEntry { Path = path, DisplayName = null, Pinned = false }],
        }));
        f.ViewModel.Refresh();

        Assert.Equal("rot-leaf", Assert.Single(f.ViewModel.Cards).RotatingNamesText);
    }

    [Fact]
    public void ACorruptCardHasNoFolderLine()
    {
        using var f = new LauncherFixture();
        f.SeedCorrupt();
        f.ViewModel.Refresh();

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.True(card.IsCorrupt);
        Assert.Equal(string.Empty, card.RotatingNamesText);
        Assert.Equal(string.Empty, card.FolderNamesText);
    }
}
