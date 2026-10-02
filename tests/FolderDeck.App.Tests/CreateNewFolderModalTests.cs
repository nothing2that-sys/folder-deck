using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;






public sealed class CreateNewFolderModalTests
{

    [Fact]
    public async Task DefaultNameInThePromptIsTheSuggestion()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        var expected = FolderPanelViewModel.SuggestNewFolderName(panel.CurrentPath!);
        Assert.Equal(expected, Assert.Single(f.Prompt.NamesAsked));
    }


    [Fact]
    public async Task CancellingCreatesNoFolder()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "새 폴더")));
    }


    [Fact]
    public async Task TypingANameCreatesTheFolder()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "새 폴더";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "새 폴더")));
    }


    [Fact]
    public async Task AnExistingNameIsRejectedAndTheReasonIsShown()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        Assert.Contains("이미 있는 이름", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);


        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Recipe", "Recipe.cs")));
    }




    [Fact]
    public async Task AcceptButtonTextIsMakeIt()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        Assert.Equal("만들기", Assert.Single(f.Prompt.AcceptButtonTextsAsked));
    }
}
