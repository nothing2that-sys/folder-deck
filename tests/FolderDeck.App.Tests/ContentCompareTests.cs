namespace FolderDeck.App.Tests;

public sealed class ContentCompareTests
{
    private static async Task<MainWindowFixture> InitializedFixtureAsync()
    {
        var f = new MainWindowFixture();

        File.WriteAllText(Path.Combine(f.CodePath, "shared.txt"), "left");
        File.WriteAllText(Path.Combine(f.OutputPath, "SHARED.txt"), "right");

        File.WriteAllText(Path.Combine(f.CodePath, "onlyleft.txt"), "x");

        File.WriteAllText(Path.Combine(f.CodePath, "dirclash.txt"), "x");
        Directory.CreateDirectory(Path.Combine(f.OutputPath, "dirclash.txt"));

        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task ReturnsNullWhenNoComparisonIsActive()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var item = code.Items.Single(i => i.Name == "shared.txt");

        Assert.Null(f.ViewModel.FindCompareCounterpart(code, item));
    }

    [Fact]
    public async Task FindsTheCounterpartInThePeerTileByNameCaseInsensitive()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        var item = code.Items.Single(i => i.Name == "shared.txt");
        var counterpart = f.ViewModel.FindCompareCounterpart(code, item);

        Assert.NotNull(counterpart);
        Assert.Same(output.Items.Single(i => i.Name == "SHARED.txt"), counterpart);
    }

    [Fact]
    public async Task WorksFromEitherSideOfTheComparison()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        var item = output.Items.Single(i => i.Name == "SHARED.txt");
        var counterpart = f.ViewModel.FindCompareCounterpart(output, item);

        Assert.Same(code.Items.Single(i => i.Name == "shared.txt"), counterpart);
    }

    [Fact]
    public async Task ReturnsNullWhenTheCounterpartIsMissingInThePeerTile()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        var item = code.Items.Single(i => i.Name == "onlyleft.txt");

        Assert.Null(f.ViewModel.FindCompareCounterpart(code, item));
    }

    [Fact]
    public async Task ReturnsNullWhenTheSameNameExistsOnlyAsADirectory()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        var item = code.Items.Single(i => i.Name == "dirclash.txt");

        Assert.Null(f.ViewModel.FindCompareCounterpart(code, item));
    }
}
