namespace FolderDeck.App.Tests;

public sealed class TrashTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    [Fact]
    public async Task EverySelectedItemGoesToTheRecycleBinAndTheCountIsAnnounced()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll", "b.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Equal(
            [Path.Combine(f.OutputPath, "a.dll"), Path.Combine(f.OutputPath, "b.dll")],
            f.RecycleBin.Sent.Order());
        Assert.Empty(Directory.GetFiles(f.OutputPath));

        Assert.Contains("2개를 휴지통으로 보냈다", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public async Task NoConfirmationStandsInTheWay()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.Prompt.Messages);
        Assert.Single(f.RecycleBin.Sent);
    }

    [Fact]
    public async Task AFolderGoesWhole()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Recipe");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Equal(Path.Combine(f.CodePath, "Recipe"), Assert.Single(f.RecycleBin.Sent));
        Assert.False(Directory.Exists(Path.Combine(f.CodePath, "Recipe")));
    }

    [Fact]
    public async Task NothingHappensWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanTrashSelection);
        Assert.False(f.ViewModel.TrashSelectionCommand.CanExecute(null));

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.RecycleBin.Sent);
        Assert.Null(f.ViewModel.Message);
    }

    [Fact]
    public async Task AFailureIsReportedAndTheSummaryStays()
    {
        using var f = await ReadyAsync();
        f.RecycleBin.Error = "다른 프로그램이 쓰고 있다";
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Contains("실패 1", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("다른 프로그램이 쓰고 있다", Assert.Single(f.Prompt.Reports));

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "a.dll")));
    }

    [Fact]
    public async Task TheListRefreshesAfterTrashing()
    {
        using var f = await ReadyAsync();
        var output = f.ViewModel.Panels[1];
        Assert.Equal(2, output.Items.Count);

        Select(output, "a.dll");
        await f.ViewModel.TrashSelectionAsync();

        Assert.Single(output.Items);
        Assert.DoesNotContain("a.dll", output.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task PermanentDeleteIsRefusedWithAReason()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        f.ViewModel.RejectPermanentDelete();

        Assert.Contains("영구 삭제는 하지 않는다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        Assert.True(f.ViewModel.MessageIsTransient);
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "a.dll")));
        Assert.Empty(f.RecycleBin.Sent);
    }
}
