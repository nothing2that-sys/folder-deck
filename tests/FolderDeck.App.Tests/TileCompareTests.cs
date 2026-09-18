using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 간 비교(기능 후보 ①, 2026-09-18 설계, doc/FeatureCandidates_2026-09-18_design.md
/// §①) — Ctrl+D(<c>FolderPanelViewModel.StartCompareCommand</c>)부터
/// <c>MainViewModel</c>의 배지 적용·해제까지.
/// </summary>
public sealed class TileCompareTests
{
    private static async Task<MainWindowFixture> InitializedFixtureAsync()
    {
        var f = new MainWindowFixture();

        // 비교가 "다름"·"같다"까지 실제로 갈리는지 보려고 두 상시 폴더에 겹치는 이름을 심는다.
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(f.CodePath, "shared-same.txt"), "same content");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-same.txt"), "same content");
        File.SetLastWriteTimeUtc(Path.Combine(f.CodePath, "shared-same.txt"), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(f.OutputPath, "shared-same.txt"), stamp);

        File.WriteAllText(Path.Combine(f.CodePath, "shared-diff.txt"), "a");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-diff.txt"), "bb"); // 크기가 다르다

        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task ComparingTwoTilesAppliesBadgesToBothSides()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0; // 유일한 다른 타일(산출물)
        code.StartCompareCommand.Execute(null);

        Assert.True(f.ViewModel.IsComparing);
        Assert.Same(f.ViewModel.Tiles[0], f.ViewModel.ComparePeerA);
        Assert.Same(f.ViewModel.Tiles[1], f.ViewModel.ComparePeerB);

        Assert.Equal(
            CompareStatus.Same,
            code.Items.Single(i => i.Name == "shared-same.txt").CompareStatus);
        Assert.Equal(
            CompareStatus.Same,
            output.Items.Single(i => i.Name == "shared-same.txt").CompareStatus);

        Assert.Equal(
            CompareStatus.Different,
            code.Items.Single(i => i.Name == "shared-diff.txt").CompareStatus);

        Assert.Equal(
            CompareStatus.OnlyHere,
            code.Items.Single(i => i.Name == "Main.cs").CompareStatus);
        Assert.Equal(
            CompareStatus.OnlyHere,
            output.Items.Single(i => i.Name == "a.dll").CompareStatus);
    }

    [Fact]
    public async Task OnlyOneCandidateIsOfferedExcludingTheRequestingTileAndEmptyTiles()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        // 상시 둘(code·산출물)만 열려 있고, 순환 칸은 InitializeAsync 뒤에도 비어 있다 —
        // 후보는 "산출물" 하나뿐이어야 한다.
        var options = Assert.Single(f.Prompt.PickOptionsAsked);
        Assert.Single(options);
    }

    [Fact]
    public async Task PressingCompareAgainOnAPeerTileClearsThePairing()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);
        Assert.True(f.ViewModel.IsComparing);

        output.StartCompareCommand.Execute(null); // 이미 낀 타일 — 토글 꺼짐

        Assert.False(f.ViewModel.IsComparing);
        Assert.Null(f.ViewModel.ComparePeerA);
        Assert.Null(f.ViewModel.ComparePeerB);
        Assert.All(code.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
        Assert.All(output.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
    }

    [Fact]
    public async Task RefreshingAPeerTileClearsTheStaleComparison()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);
        Assert.True(f.ViewModel.IsComparing);

        await output.RefreshAsync();

        Assert.False(f.ViewModel.IsComparing);
        Assert.All(code.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
    }

    [Fact]
    public async Task NoOtherOpenTileWarnsInsteadOfPromptingAnEmptyList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // 산출물 타일을 빼서 열린 타일을 하나(code)만 남긴다.
        f.ViewModel.IsEditingLayout = true;
        f.ViewModel.RemoveTile(f.ViewModel.Tiles[1]);

        var code = f.ViewModel.Panels[0];
        code.StartCompareCommand.Execute(null);

        Assert.Empty(f.Prompt.PickOptionsAsked);
        Assert.False(f.ViewModel.IsComparing);
    }
}
