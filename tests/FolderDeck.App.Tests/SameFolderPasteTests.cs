using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

public sealed class SameFolderPasteTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    private static string[] NamesIn(string folder) =>
        [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order()!];

    private static DataObject Cut(params string[] paths)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream([2, 0, 0, 0]));
        return data;
    }

    [Fact]
    public async Task PastingIntoTheSameFolderMakesACopyWithoutTheConflictDialog()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main (2).cs", NamesIn(f.CodePath));
        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Single(f.Prompt.Messages);
    }

    [Fact]
    public async Task RepeatedPastesKeepCountingUp()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);
        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var names = NamesIn(f.CodePath);
        Assert.Contains("Main (2).cs", names);
        Assert.Contains("Main (3).cs", names);
    }

    [Fact]
    public async Task TheOriginalIsUntouched()
    {
        using var f = await ReadyAsync();
        var original = Path.Combine(f.CodePath, "Main.cs");
        var before = File.ReadAllText(original);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, File.ReadAllText(original));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.CodePath, "Main (2).cs")));
    }

    [Fact]
    public async Task AFolderIsDuplicatedInPlaceToo()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Recipe");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Recipe (2)", NamesIn(f.CodePath));

        Assert.Equal(["Recipe.cs"], NamesIn(Path.Combine(f.CodePath, "Recipe (2)")));
        Assert.Equal(["Recipe.cs"], NamesIn(Path.Combine(f.CodePath, "Recipe")));
    }

    [Theory]
    [InlineData(ConflictPolicy.Overwrite)]
    [InlineData(ConflictPolicy.Skip)]
    public async Task ARememberedPolicyDoesNotApplyHere(ConflictPolicy remembered)
    {
        using var f = await ReadyAsync();

        f.Workspace.OnConflict = remembered;
        f.Workspace.AskOnConflict = false;

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main (2).cs", NamesIn(f.CodePath));
        Assert.Empty(f.Prompt.ConflictMessages);
    }

    [Fact]
    public async Task TheWorkspacePolicyIsNotTouched()
    {
        using var f = await ReadyAsync();
        Assert.Null(f.Workspace.OnConflict);
        Assert.Null(f.Workspace.AskOnConflict);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Null(f.Workspace.OnConflict);
        Assert.Null(f.Workspace.AskOnConflict);
    }

    [Fact]
    public async Task CuttingAndPastingIntoTheSameFolderStillDoesNothing()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        var before = NamesIn(f.CodePath);

        f.Clipboard.Data = Cut(Path.Combine(f.CodePath, "Main.cs"));

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, NamesIn(f.CodePath));
        Assert.Contains("붙여넣을 것이 없다", f.ViewModel.Message);
    }

    [Fact]
    public async Task PastingAFolderIntoItselfIsStillRejected()
    {
        using var f = await ReadyAsync();
        var recipe = Path.Combine(f.CodePath, "Recipe");

        var code = Tile(f, f.CodePath);
        await code.NavigateToCommand.ExecuteAsync(recipe);

        f.Clipboard.Data = ClipboardService.FileDataObject([new OperationItem(recipe, true)]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(["Recipe.cs"], NamesIn(recipe));
        Assert.Contains("붙여넣을 것이 없다", f.ViewModel.Message);
    }

    [Fact]
    public async Task AMixedBatchFallsBackToTheOldRule()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);

        f.Clipboard.Data = ClipboardService.FileDataObject([
            new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false),
            new OperationItem(Path.Combine(f.CodePath, "Recipe", "Recipe.cs"), false),
        ]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var names = NamesIn(f.CodePath);

        Assert.Contains("Recipe.cs", names);

        Assert.DoesNotContain("Main (2).cs", names);
    }

    [Fact]
    public async Task TheSkippedCountIsReportedInAMixedBatch()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);

        f.Clipboard.Data = ClipboardService.FileDataObject([
            new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false),
            new OperationItem(Path.Combine(f.CodePath, "Recipe", "Recipe.cs"), false),
        ]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("같은 폴더", f.ViewModel.Message);
    }

    [Fact]
    public async Task TheConfirmationSaysWhatNameWillAppear()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("Main.cs → Main (2).cs", message);
        Assert.Contains("원본은 그대로 남습니다", message);
    }

    [Fact]
    public async Task ThePreviewAgreesWithWhatActuallyGetsCreated()
    {
        using var f = await ReadyAsync();
        File.WriteAllText(Path.Combine(f.CodePath, "Main (2).cs"), "이미 있다");

        var code = Tile(f, f.CodePath);
        await code.RefreshAsync();
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main.cs → Main (3).cs", Assert.Single(f.Prompt.Messages));
        Assert.Contains("Main (3).cs", NamesIn(f.CodePath));
    }

    [Fact]
    public async Task CancellingCreatesNothing()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        var before = NamesIn(f.CodePath);

        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        f.Prompt.Answer = false;
        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, NamesIn(f.CodePath));
        Assert.Contains("취소", f.ViewModel.Message);
    }

    [Fact]
    public async Task EveryItemGetsItsOwnLineWhenThereAreFew()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs", "Recipe");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("사본 2개를 만듭니다", message);
        Assert.Contains("Main.cs → Main (2).cs", message);
        Assert.Contains("Recipe → Recipe (2)", message);
        Assert.DoesNotContain("외 ", message);
    }

    [Fact]
    public async Task ManyItemsFoldIntoACountButAllOfThemAreStillCopied()
    {
        using var f = await ReadyAsync();
        var folder = Path.Combine(f.CodePath, "many");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"f{i}.txt"), "x");
        }

        var code = Tile(f, f.CodePath);
        await code.NavigateToCommand.ExecuteAsync(folder);
        code.SelectedItems = [.. code.Items];
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("사본 8개를 만듭니다", message);
        Assert.Contains("외 3개", message);

        Assert.Equal(16, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public async Task PastingIntoAnotherFolderDoesNotGetThisConfirmation()
    {
        using var f = await ReadyAsync();
        var output = Tile(f, f.OutputPath);

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await output.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.Messages);
        Assert.Contains("Main.cs", NamesIn(f.OutputPath));
    }
}
