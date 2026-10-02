using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;










public sealed class ClipboardCopyPasteTests
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
    public async Task CopyPutsEveryPickedPathOnTheClipboard()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];
        Select(panel, "Recipe", "Main.cs");

        panel.CopySelectionToClipboardCommand.Execute(null);

        var picked = Assert.Single(f.Clipboard.FileSets);

        Assert.Equal(
            [Path.Combine(f.CodePath, "Main.cs"), Path.Combine(f.CodePath, "Recipe")],
            picked.Select(i => i.Path).Order());

        Assert.True(picked.Single(i => i.Path.EndsWith("Recipe")).IsDirectory);
    }








    [Fact]
    public void WhatCopyPutsOnTheClipboardCarriesTheShellFormatAndSaysCopy()
    {
        var data = ClipboardService.FileDataObject([
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
        ]);

        Assert.Equal(
            [@"C:\deck\work\Main.cs", @"C:\deck\work\Views"],
            Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));

        var effect = Assert.IsType<MemoryStream>(data.GetData(ShellExport.PreferredDropEffectFormat));

        Assert.Equal([1, 0, 0, 0], effect.ToArray());
    }








    [Fact]
    public void WhatCutPutsOnTheClipboardCarriesTheShellFormatAndSaysMove()
    {
        var data = ClipboardService.FileDataObject(
            [
                new OperationItem(@"C:\deck\work\Main.cs", false),
                new OperationItem(@"C:\deck\work\Views", true),
            ],
            move: true);

        Assert.Equal(
            [@"C:\deck\work\Main.cs", @"C:\deck\work\Views"],
            Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));

        var effect = Assert.IsType<MemoryStream>(data.GetData(ShellExport.PreferredDropEffectFormat));

        Assert.Equal([2, 0, 0, 0], effect.ToArray());
    }



    [Fact]
    public async Task CopyingNothingIsRejectedAndLeavesTheClipboardAlone()
    {
        using var f = await ReadyAsync();

        f.ViewModel.Panels[0].CopySelectionToClipboardCommand.Execute(null);

        Assert.Empty(f.Clipboard.FileSets);
        Assert.Null(f.Clipboard.Data);
        Assert.Contains("복사할 것이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);


        Assert.True(f.ViewModel.MessageIsTransient);
    }


    private static FolderPanelViewModel Bare(
        Action<string>? reportError, Action<string>? reportRejection) =>
        new(
            new FolderEnumerator(),
            new FakeShellLauncher(),
            new FakeClipboardService(),
            isRotating: false,
            isSearchTile: false,
            reportError,
            reportRejection);


    [Fact]
    public void CopyingNothingGoesDownTheRejectionChannelNotTheFailureOne()
    {
        var failures = new List<string>();
        var rejections = new List<string>();
        var panel = Bare(failures.Add, rejections.Add);

        panel.CopySelectionToClipboard();

        Assert.Contains("복사할 것이 없다", Assert.Single(rejections));
        Assert.Empty(failures);
    }





    [Fact]
    public void WithoutARejectionChannelTheRejectionFallsBackToTheFailureOne()
    {
        var failures = new List<string>();
        var panel = Bare(failures.Add, reportRejection: null);

        panel.CopySelectionToClipboard();

        Assert.Contains("복사할 것이 없다", Assert.Single(failures));
    }


    [Fact]
    public async Task AFailedClipboardWriteIsToldToTheUser()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];
        Select(panel, "Main.cs");
        f.Clipboard.Error = "클립보드가 잠겨 있다";

        panel.CopySelectionToClipboardCommand.Execute(null);

        Assert.Contains("클립보드에 올리지 못했다", f.ViewModel.Message);
        Assert.Contains("클립보드가 잠겨 있다", f.ViewModel.Message);
    }




    [Fact]
    public async Task CopyPathStillPutsTextOnTheClipboardAndIsADifferentCommand()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];

        panel.CopyItemPathCommand.Execute(panel.Items.Single(i => i.Name == "Main.cs"));

        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), f.Clipboard.Text);
        Assert.Empty(f.Clipboard.FileSets);
    }




    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);


    private static DataObject Cut(params string[] paths)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths, autoConvert: true);
        data.SetData(
            ShellExport.PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(2u)));
        return data;
    }




    [Fact]
    public async Task WhatComesOffTheClipboardIsParsedByTheExistingShellParser()
    {
        using var f = await ReadyAsync();
        var panel = Tile(f, f.CodePath);
        Select(panel, "Recipe", "Main.cs");
        panel.CopySelectionToClipboardCommand.Execute(null);

        var items = DropRouter.ShellItems(f.Clipboard.GetData()!);

        Assert.Equal(2, items.Count);
        Assert.True(items.Single(i => i.Path.EndsWith("Recipe")).IsDirectory);
        Assert.False(items.Single(i => i.Path.EndsWith("Main.cs")).IsDirectory);
    }


    [Fact]
    public async Task PasteCopiesTheClipboardFilesIntoTheFolderTheTileIsShowing()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        var views = Path.Combine(f.WorkPath, "Views");
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(views, "Main.cs")));


        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Empty(f.Prompt.Messages);
    }


    [Fact]
    public async Task PasteReportsThroughTheExistingResultPath()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣기 — 성공 1 · 건너뜀 0 · 실패 0", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }




    [Fact]
    public async Task PasteMovesWhenTheClipboardSaysCutAndRemovesTheOriginal()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = Cut(Path.Combine(f.CodePath, "Main.cs"));

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Contains("붙여넣기 — 성공 1 · 건너뜀 0 · 실패 0", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Empty(f.Prompt.Messages);
    }


    [Fact]
    public async Task PasteWithAnEmptyClipboardIsRejected()
    {
        using var f = await ReadyAsync();
        var before = Directory.GetFiles(f.OutputPath).Length;

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, Directory.GetFiles(f.OutputPath).Length);
        Assert.Contains("붙여넣을 파일이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }


    [Fact]
    public async Task PasteIsRejectedWhenTheTileHasNoDestination()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Rotating.ShellDropDestination);

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣을 곳이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }




    [Fact]
    public async Task PasteIsRejectedOnATileShowingAnUnreachableFolder()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣을 곳이 없다", f.ViewModel.Message);
    }










    [Fact]
    public async Task PastingBackIntoTheFolderTheItemsCameFromMakesACopy()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Main (2).cs", "Main.cs"],
            Directory.GetFiles(f.CodePath).Select(Path.GetFileName).Order());


        Assert.Equal(
            File.ReadAllText(Path.Combine(f.CodePath, "Main.cs")),
            File.ReadAllText(Path.Combine(f.CodePath, "Main (2).cs")));
        Assert.False(f.ViewModel.MessageIsWarning);
    }






    [Fact]
    public async Task PasteAsksWhenNamesCollide()
    {
        using var f = await ReadyAsync();
        File.WriteAllText(Path.Combine(f.OutputPath, "Main.cs"), "old");

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Contains("같은 이름으로 이미 있습니다", f.Prompt.ConflictMessages[0]);
        Assert.Equal(ConflictPolicy.Overwrite, f.Prompt.SuggestedPolicy);
    }


    [Fact]
    public async Task PasteDoesNotAskWhenNothingCollides()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }


    [Fact]
    public void EveryTilePanelHasThePasteChannelWired()
    {
        using var f = new MainWindowFixture();

        Assert.All(f.ViewModel.Panels, p => Assert.NotNull(p.PasteInto));
    }











    [Fact]
    public void TheMainViewModelConstructorStillTakesTenRequiredArguments()
    {
        var constructor = Assert.Single(typeof(MainViewModel).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Equal(10, parameters.Count(p => !p.IsOptional));
        Assert.All(parameters.Skip(10), p => Assert.True(p.IsOptional));
    }
}
