using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Tests;

/// <summary>런처 카드 — 정체성 블록·폴더 구성 칩·필터·편집 (§3.1).</summary>
public sealed class LauncherCardTests
{
    private static Workspace SeedMixed(LauncherFixture f, string title, int pinned, int rotating)
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

        var workspace = new Workspace { Title = title, LastUsed = DateTimeOffset.Now, Folders = folders };
        Assert.Null(f.Store.SaveWorkspace(workspace));
        f.ViewModel.Refresh();
        return workspace;
    }

    // ── 폴더 구성 칩 ─────────────────────────────────────────────────────────

    [Fact]
    public void ChipsSeparatePinnedNamesFromTheRotatingCount()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "구성", pinned: 2, rotating: 3);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal(["pin0", "pin1"], card.PinnedChips);
        Assert.Equal(3, card.RotatingCount);
        Assert.Equal("+ 순환 3", card.RotatingChipText);
        Assert.True(card.HasRotatingChip);
        Assert.False(card.HasMorePinned);
    }

    [Fact]
    public void PinnedChipsAreCappedAndTheRestBecomeACount()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "많음", pinned: 7, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.Equal(4, card.PinnedChips.Count);
        Assert.True(card.HasMorePinned);
        Assert.Equal("+3", card.MorePinnedText);
        Assert.False(card.HasRotatingChip);
    }

    [Fact]
    public void NoRotatingChipWhenEverythingIsPinned()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "전부 상시", pinned: 2, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.False(card.HasRotatingChip);
        Assert.Equal(0, card.RotatingCount);
    }

    [Fact]
    public void AWorkspaceWithNoFoldersSaysSo()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "빈 것", pinned: 0, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.True(card.HasNoFolders);
        Assert.Empty(card.PinnedChips);
        Assert.False(card.HasRotatingChip);
    }

    [Fact]
    public void ChipNameFallsBackToTheFolderLeafWhenDisplayNameIsBlank()
    {
        using var f = new LauncherFixture();
        var path = f.MakeRealFolder("leaf-name");
        var workspace = new Workspace
        {
            Title = "이름 없음",
            Folders = [new FolderEntry { Path = path, DisplayName = null, Pinned = true }],
        };
        Assert.Null(f.Store.SaveWorkspace(workspace));
        f.ViewModel.Refresh();

        Assert.Equal("leaf-name", Assert.Single(f.ViewModel.Cards).PinnedChips[0]);
    }

    // ── 대표 경로 ────────────────────────────────────────────────────────────

    [Fact]
    public void PrimaryPathIsTheFirstFolder()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "경로", pinned: 1, rotating: 1);

        Assert.Equal(seeded.Folders[0].Path, Assert.Single(f.ViewModel.Cards).PrimaryPath);
    }

    /// <summary>폴더가 없으면 워크스페이스 파일 위치를 보여준다 — 빈 줄로 두지 않는다.</summary>
    [Fact]
    public void PrimaryPathFallsBackToTheWorkspaceFile()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "폴더 없음", pinned: 0, rotating: 0);

        Assert.Equal(f.Paths.WorkspaceFile(seeded.Id), Assert.Single(f.ViewModel.Cards).PrimaryPath);
    }

    // ── "열려 있음" 배지는 Phase 4 자리 ──────────────────────────────────────

    [Fact]
    public void OpenBadgeIsAlwaysOffUntilPhase4()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "배지", pinned: 1, rotating: 0);

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        // 렌더 자리만 있고 실제 상태를 계산하지 않는다.
        Assert.False(f.ViewModel.Cards[0].IsOpen);
    }

    // ── 필터 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FilterNarrowsByTitle()
    {
        using var f = new LauncherFixture();
        f.Seed("EIS Item 개발");
        f.Seed("주차 카메라");
        f.ViewModel.Refresh();
        Assert.Equal(2, f.ViewModel.Cards.Count);

        f.ViewModel.FilterText = "카메라";

        Assert.Equal("주차 카메라", Assert.Single(f.ViewModel.Cards).Title);
        Assert.True(f.ViewModel.HasFilter);
    }

    [Fact]
    public void FilterAlsoMatchesThePath()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "알파", pinned: 1, rotating: 0);
        SeedMixed(f, "베타", pinned: 1, rotating: 0);

        // 경로에는 "베타-pin0" 이 들어 있다.
        f.ViewModel.FilterText = "베타-pin0";

        Assert.Equal("베타", Assert.Single(f.ViewModel.Cards).Title);
    }

    [Fact]
    public void FilterIsCaseInsensitiveAndPartial()
    {
        using var f = new LauncherFixture();
        f.Seed("EIS Item 개발");
        f.Seed("주차 카메라");
        f.ViewModel.Refresh();

        f.ViewModel.FilterText = "eis";
        Assert.Single(f.ViewModel.Cards);

        f.ViewModel.FilterText = "ITEM";
        Assert.Single(f.ViewModel.Cards);
    }

    [Fact]
    public void ClearingTheFilterRestoresEverything()
    {
        using var f = new LauncherFixture();
        f.Seed("하나");
        f.Seed("둘");
        f.ViewModel.Refresh();

        f.ViewModel.FilterText = "하나";
        Assert.Single(f.ViewModel.Cards);

        f.ViewModel.FilterText = string.Empty;
        Assert.Equal(2, f.ViewModel.Cards.Count);
        Assert.False(f.ViewModel.HasFilter);
    }

    /// <summary>"저장된 게 없다"와 "필터에 안 걸린다"는 다른 상태다.</summary>
    [Fact]
    public void NoMatchesIsDistinctFromNoWorkspaces()
    {
        using var f = new LauncherFixture();
        Assert.True(f.ViewModel.HasNoWorkspaces);
        Assert.False(f.ViewModel.HasNoMatches);

        f.Seed("하나");
        f.Seed("둘");
        f.ViewModel.Refresh();
        f.ViewModel.FilterText = "없는이름";

        Assert.False(f.ViewModel.HasNoWorkspaces);
        Assert.True(f.ViewModel.HasNoMatches);
        Assert.Empty(f.ViewModel.Cards);
    }

    [Fact]
    public void FilterRowOnlyMattersWithMoreThanOneCard()
    {
        using var f = new LauncherFixture();
        Assert.False(f.ViewModel.ShowFilter);

        f.Seed("하나");
        f.ViewModel.Refresh();
        Assert.False(f.ViewModel.ShowFilter);

        f.Seed("둘");
        f.ViewModel.Refresh();
        Assert.True(f.ViewModel.ShowFilter);
    }

    [Fact]
    public void FilterSurvivesARefresh()
    {
        using var f = new LauncherFixture();
        f.Seed("하나");
        f.Seed("둘");
        f.ViewModel.Refresh();
        f.ViewModel.FilterText = "둘";

        f.ViewModel.Refresh();

        Assert.Equal("둘", Assert.Single(f.ViewModel.Cards).Title);
    }

    // ── 수정 모드 ────────────────────────────────────────────────────────────

    [Fact]
    public void EditModeTogglesAndStillPersists()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("고치기 전");
        f.ViewModel.Refresh();
        var card = f.ViewModel.Cards[0];

        Assert.False(card.IsEditing);
        f.ViewModel.ToggleEdit(card);
        Assert.True(card.IsEditing);

        card.Title = "고친 뒤";
        f.ViewModel.ToggleEdit(card);
        Assert.False(card.IsEditing);

        f.Store.Flush();
        Assert.Equal("고친 뒤", f.Store.LoadWorkspace(seeded.Id).ValueOrThrow().Title);
    }

    [Fact]
    public void CorruptCardsCannotEnterEditMode()
    {
        using var f = new LauncherFixture();
        f.SeedCorrupt();
        f.ViewModel.Refresh();

        f.ViewModel.ToggleEdit(f.ViewModel.Cards[0]);

        Assert.False(f.ViewModel.Cards[0].IsEditing);
    }

    // ── 손상 카드의 유일한 액션 ──────────────────────────────────────────────

    [Fact]
    public void RevealFileShowsTheUnreadableFileInExplorer()
    {
        using var f = new LauncherFixture();
        var corruptPath = f.SeedCorrupt();
        f.ViewModel.Refresh();

        f.ViewModel.RevealFile(f.ViewModel.Cards[0]);

        var (path, isDirectory) = Assert.Single(f.Shell.Revealed);
        Assert.Equal(corruptPath, path);
        Assert.False(isDirectory);   // → explorer /select,"..."
    }

    [Fact]
    public void RevealDoesNothingForHealthyCards()
    {
        using var f = new LauncherFixture();
        f.Seed("정상");
        f.ViewModel.Refresh();

        f.ViewModel.RevealFile(f.ViewModel.Cards[0]);

        Assert.Empty(f.Shell.Revealed);
    }

    [Fact]
    public void RevealFailureIsSurfaced()
    {
        using var f = new LauncherFixture();
        f.SeedCorrupt();
        f.ViewModel.Refresh();
        f.Shell.Error = "탐색기를 실행할 수 없다";

        f.ViewModel.RevealFile(f.ViewModel.Cards[0]);

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("탐색기를 실행할 수 없다", f.ViewModel.Message);
    }

    /// <summary>Stage 3 문구 유지 — 실제 폴더가 안 지워진다는 말이 확인창에 남아 있다.</summary>
    [Fact]
    public void DeleteStillSpellsOutThatRealFoldersSurvive()
    {
        using var f = new LauncherFixture();
        f.Seed("문구");
        f.ViewModel.Refresh();

        f.ViewModel.Delete(f.ViewModel.Cards[0]);

        Assert.Contains("실제 폴더는 지워지지 않습니다", Assert.Single(f.Prompt.Messages));
    }
}
