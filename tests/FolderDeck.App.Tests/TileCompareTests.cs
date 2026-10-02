using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

public sealed class TileCompareTests
{
    private static async Task<MainWindowFixture> InitializedFixtureAsync()
    {
        var f = new MainWindowFixture();

        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(f.CodePath, "shared-same.txt"), "same content");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-same.txt"), "same content");
        File.SetLastWriteTimeUtc(Path.Combine(f.CodePath, "shared-same.txt"), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(f.OutputPath, "shared-same.txt"), stamp);

        File.WriteAllText(Path.Combine(f.CodePath, "shared-diff.txt"), "a");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-diff.txt"), "bb");

        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task ComparingTwoTilesAppliesBadgesToBothSides()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
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

        output.StartCompareCommand.Execute(null);

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

        f.ViewModel.IsEditingLayout = true;
        f.ViewModel.RemoveTile(f.ViewModel.Tiles[1]);

        var code = f.ViewModel.Panels[0];
        code.StartCompareCommand.Execute(null);

        Assert.Empty(f.Prompt.PickOptionsAsked);
        Assert.False(f.ViewModel.IsComparing);
    }

    [Fact]
    public async Task ComparingAPinnedTileAgainstARotatingTileAlsoAppliesBadges()
    {
        using var f = new MainWindowFixture();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(f.CodePath, "shared.txt"), "same");
        File.WriteAllText(Path.Combine(f.DocsPath, "shared.txt"), "same");

        File.SetLastWriteTimeUtc(Path.Combine(f.CodePath, "shared.txt"), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(f.DocsPath, "shared.txt"), stamp);

        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var code = f.ViewModel.Panels[0];
        var rotating = f.Rotating;

        f.Prompt.PickAnswer = 1;
        code.StartCompareCommand.Execute(null);

        Assert.True(f.ViewModel.IsComparing);
        Assert.Equal(
            CompareStatus.Same,
            code.Items.Single(i => i.Name == "shared.txt").CompareStatus);
        Assert.Equal(
            CompareStatus.Same,
            rotating.Items.Single(i => i.Name == "shared.txt").CompareStatus);
    }

    [Fact]
    public async Task IsShowingComparePickerIsTrueOnlyWhileThePickerDialogWouldBeOpen()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        bool? duringCall = null;
        f.Prompt.OnPickOneCalled = () => duringCall = f.ViewModel.IsShowingComparePicker;
        f.Prompt.PickAnswer = 0;

        Assert.False(f.ViewModel.IsShowingComparePicker);
        code.StartCompareCommand.Execute(null);

        Assert.True(duringCall);
        Assert.False(f.ViewModel.IsShowingComparePicker);
    }
}
