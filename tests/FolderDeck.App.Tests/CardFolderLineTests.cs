using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 런처 카드 3줄째 — 폴더 개수 · 상시 이름 · 순환 이름 (Phase 8-B ④).
/// </summary>
/// <remarks>
/// 그 전에는 순환이 개수 칩("+ 순환 3") 하나였다. 개수만으로는 어느 워크스페이스인지 알아볼 수
/// 없어서 — 상시가 하나도 없는 워크스페이스는 3줄이 통째로 숫자였다 — 상시와 같이 이름을 적는다.
/// </remarks>
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

    /// <summary>줄 맨 앞이 개수다 — 뒤에 오는 이름들이 무엇의 목록인지 먼저 말한다.</summary>
    [Fact]
    public void TheCountLeadsTheLine()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "구성", pinned: 2, rotating: 3);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal("폴더 5 · pin0 · pin1 · rot0 · rot1 · rot2", card.FolderNamesText);
        Assert.StartsWith(card.FolderCountText, card.FolderNamesText);
    }

    /// <summary>
    /// 구분점은 조각이 들고 온다 — 앞 조각이 비었을 때 점만 뜬 줄이 생기지 않게.
    /// </summary>
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

    /// <summary>순환도 상시와 <b>같은 상한</b>으로 잘리고 나머지는 개수로 접힌다.</summary>
    [Fact]
    public void RotatingNamesAreCappedTheSameWayPinnedOnesAre()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "많음", pinned: 0, rotating: 7);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal("rot0 · rot1 · rot2 · rot3 +3", card.RotatingNamesText);
    }

    /// <summary>폴더가 하나도 없으면 줄 자체가 비어 있고, 화면은 "등록된 폴더 없음"을 쓴다.</summary>
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

    /// <summary>표시 이름이 비어 있으면 경로의 잎 이름으로 떨어진다 — 상시 쪽과 같은 규칙이다.</summary>
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

    /// <summary>읽지 못한 파일에는 폴더 줄이 없다 — 아무것도 읽어내지 못했다.</summary>
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
