using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

/// <summary>
/// 「새 폴더 만들기」 모달 배선 (§4.6 · D75-c 6-2). <c>SuggestNewFolderName</c>(기본값) →
/// <c>IPrompt.AskName</c>(모달) → <c>CreateNewFolder</c>(Core 실제 생성)를 잇는 자리다.
/// 메뉴 XAML 배선(6-1)은 이 시험이 못 문다 — 여기서는 이 계층만 문다.
/// </summary>
public sealed class CreateNewFolderModalTests
{
    // 1
    [Fact]
    public async Task DefaultNameInThePromptIsTheSuggestion()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null; // 취소 — 기본값이 무엇으로 채워졌는지만 본다
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        var expected = FolderPanelViewModel.SuggestNewFolderName(panel.CurrentPath!);
        Assert.Equal(expected, Assert.Single(f.Prompt.NamesAsked));
    }

    // 2
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

    // 3
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

    // 4
    [Fact]
    public async Task AnExistingNameIsRejectedAndTheReasonIsShown()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe"; // "work" 안에 이미 있는 하위 폴더 이름
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CreateNewFolderInteractiveCommand.Execute(null);

        Assert.Contains("이미 있는 이름", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        // 내용은 그대로 — 거절이 파일 시스템을 건드리지 않는다(FolderCreatorTests 와 같은 결).
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Recipe", "Recipe.cs")));
    }
}
