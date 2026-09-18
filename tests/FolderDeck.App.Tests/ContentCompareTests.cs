namespace FolderDeck.App.Tests;

/// <summary>
/// 내용 비교(기능 후보 ①의 연장, 2026-09-18) — 우클릭 「내용 비교」가 상대 타일에서 짝을
/// 찾는 <c>MainViewModel.FindCompareCounterpart</c>만 본다. diff 계산 자체는
/// <c>FolderDeck.Core.Tests.FileContentDifferTests</c>가, 파일 읽기는
/// <c>FileContentReaderTests</c>가 이미 순수 함수로 덮는다 — 여기서는 "비교 중인 상대
/// 타일에서 실제로 짝을 찾아오는가"만 확인한다.
/// </summary>
public sealed class ContentCompareTests
{
    private static async Task<MainWindowFixture> InitializedFixtureAsync()
    {
        var f = new MainWindowFixture();

        File.WriteAllText(Path.Combine(f.CodePath, "shared.txt"), "left");
        File.WriteAllText(Path.Combine(f.OutputPath, "SHARED.txt"), "right"); // 대소문자만 다름

        File.WriteAllText(Path.Combine(f.CodePath, "onlyleft.txt"), "x");

        File.WriteAllText(Path.Combine(f.CodePath, "dirclash.txt"), "x");
        Directory.CreateDirectory(Path.Combine(f.OutputPath, "dirclash.txt")); // 이름은 같지만 폴더

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
