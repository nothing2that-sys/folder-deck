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

    // ── "열려 있음" 배지 + "창 보이기" (§4.1 · §4.2) ─────────────────────────

    [Fact]
    public void ClosedWorkspacesShowNoBadgeAndAnOpenButton()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "닫힘", pinned: 1, rotating: 0);

        var card = Assert.Single(f.ViewModel.Cards);

        Assert.False(card.IsOpen);
        Assert.Equal("열기", card.OpenButtonText);
    }

    [Fact]
    public void AnOpenWorkspaceGetsTheBadgeAndTheShowButton()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "열림", pinned: 1, rotating: 0);
        f.Signals.Listening.Add(seeded.Id);

        f.ViewModel.RefreshOpenState();

        var card = Assert.Single(f.ViewModel.Cards);
        Assert.True(card.IsOpen);
        Assert.Equal("창 보이기", card.OpenButtonText);
    }

    /// <summary>닫히면 배지가 사라지고 버튼이 "열기" 로 돌아온다 — 재검사는 활성화 때 돈다.</summary>
    [Fact]
    public void ClosingAWorkspaceClearsTheBadgeOnTheNextCheck()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "닫는다", pinned: 1, rotating: 0);
        f.Signals.Listening.Add(seeded.Id);
        f.ViewModel.RefreshOpenState();
        Assert.True(f.ViewModel.Cards[0].IsOpen);

        f.Signals.Listening.Remove(seeded.Id);
        f.ViewModel.RefreshOpenState();

        Assert.False(f.ViewModel.Cards[0].IsOpen);
        Assert.Equal("열기", f.ViewModel.Cards[0].OpenButtonText);
    }

    /// <summary>"창 보이기" 는 새 프로세스를 띄우지 않는다 — 신호만 보낸다 (§4.2).</summary>
    [Fact]
    public void ShowWindowSignalsInsteadOfStartingAProcess()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "앞으로", pinned: 1, rotating: 0);
        f.Signals.Listening.Add(seeded.Id);
        f.ViewModel.RefreshOpenState();

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.Equal(seeded.Id, Assert.Single(f.Signals.Sent));
        Assert.Empty(f.Opened);
        Assert.True(f.ViewModel.Cards[0].IsOpen);
    }

    /// <summary>
    /// 배지가 낡아 "열려 있음" 인데 실제로 닫혀 있었다 → 누르면 그냥 새로 연다
    /// (§4.2 "판정은 누를 때").
    /// </summary>
    [Fact]
    public void AStaleOpenBadgeStillOpensANewWindow()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "낡은 배지", pinned: 1, rotating: 0);
        f.Signals.Listening.Add(seeded.Id);
        f.ViewModel.RefreshOpenState();
        Assert.Equal("창 보이기", f.ViewModel.Cards[0].OpenButtonText);

        // 재검사 없이 창이 닫혔다.
        f.Signals.Listening.Remove(seeded.Id);

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.Equal(seeded.Id, Assert.Single(f.Signals.Sent));   // 먼저 신호를 시도했고
        Assert.Equal(seeded.Id, Assert.Single(f.Opened));         // 실패해서 새로 열었다
    }

    /// <summary>배지가 비었는데 실제로 열려 있었다 → 기존 창이 앞으로 온다 (§4.2).</summary>
    [Fact]
    public void AStaleEmptyBadgeStillActivatesTheExistingWindow()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "몰랐던 창", pinned: 1, rotating: 0);
        Assert.False(f.ViewModel.Cards[0].IsOpen);

        // 재검사 없이 창이 떴다.
        f.Signals.Listening.Add(seeded.Id);

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.Empty(f.Opened);
        Assert.True(f.ViewModel.Cards[0].IsOpen);
    }

    /// <summary>폴링하지 않는다 (§4.2) — 목록을 읽을 때와 재검사를 부를 때만 잰다.</summary>
    [Fact]
    public void TheBadgeIsNotPolled()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "폴링 없음", pinned: 1, rotating: 0);

        var afterRefresh = f.Signals.QueryCount;
        f.ViewModel.FilterText = "폴";
        f.ViewModel.FilterText = string.Empty;

        Assert.Equal(afterRefresh, f.Signals.QueryCount);

        f.ViewModel.RefreshOpenState();
        Assert.Equal(afterRefresh + 1, f.Signals.QueryCount);
    }

    [Fact]
    public void FailingToStartTheProcessIsSurfaced()
    {
        using var f = new LauncherFixture();
        SeedMixed(f, "실패", pinned: 1, rotating: 0);
        f.SelfLauncher.Error = "실행 파일을 찾을 수 없다";

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("실행 파일을 찾을 수 없다", f.ViewModel.Message);
        Assert.False(f.ViewModel.Cards[0].IsOpen);
    }

    /// <summary>런처가 여는 워크스페이스는 다음 실행의 자동 열기 대상이 된다.</summary>
    [Fact]
    public void OpeningRemembersTheLastWorkspace()
    {
        using var f = new LauncherFixture();
        var seeded = SeedMixed(f, "마지막", pinned: 1, rotating: 0);

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.Equal(seeded.Id, f.Settings.LastWorkspaceId);
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
