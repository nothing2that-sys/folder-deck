using System.ComponentModel;
using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;










public sealed class ShellDropTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }


    private static IReadOnlyList<OperationItem> ShellItems(params string[] paths) =>
        [.. paths.Select(p => new OperationItem(p, Directory.Exists(p)))];




    [Fact]
    public async Task EveryItemFromTheDestinationMeansNothingToAccept()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.WorkPath, "Views"));

        Assert.Empty(MainViewModel.AcceptedForDrop(f.WorkPath, items));
        Assert.False(f.ViewModel.CanDropOnto(f.WorkPath, items));
    }




    [Fact]
    public async Task OnlyTheItemsThatCameFromTheDestinationAreDropped()
    {
        using var f = await ReadyAsync();

        var fromDocs = Path.Combine(f.DocsPath, "spec.md");
        var fromWork = Path.Combine(f.WorkPath, "Main.cs");
        var items = ShellItems(fromDocs, fromWork);

        var accepted = MainViewModel.AcceptedForDrop(f.DocsPath, items);

        Assert.Single(accepted);
        Assert.Equal(fromWork, accepted[0].Path);
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }

    [Fact]
    public async Task NothingFromTheDestinationMeansEverythingIsAccepted()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.OutputPath, "a.dll"));

        Assert.Equal(2, MainViewModel.AcceptedForDrop(f.DocsPath, items).Count);
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }





    [Fact]
    public async Task ItemsFromThreeDifferentFoldersAreJudgedOneByOne()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.DocsPath, "spec.md"),
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.OutputPath, "a.dll"),
            Path.Combine(f.CodePath, "Main.cs"));

        var accepted = MainViewModel.AcceptedForDrop(f.DocsPath, items);

        Assert.Equal(3, accepted.Count);
        Assert.DoesNotContain(accepted, i => i.Path == Path.Combine(f.DocsPath, "spec.md"));
    }


    [Fact]
    public async Task TheSkippedItemIsLeftAloneAndTheRestGoThrough()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(Path.Combine(f.DocsPath, "spec.md"), Path.Combine(f.WorkPath, "Main.cs")),
            copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "spec.md")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }


    [Fact]
    public async Task TheSkippedCountIsWrittenIntoTheResult()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(Path.Combine(f.DocsPath, "spec.md"), Path.Combine(f.WorkPath, "Main.cs")),
            copy: true);

        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.Contains("온 곳과 같은 폴더 1", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);


        Assert.Empty(f.Prompt.Reports);
    }


    [Fact]
    public async Task ADropWithNothingSkippedSaysNothingAboutSkipping()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath, ShellItems(Path.Combine(f.WorkPath, "Main.cs")), copy: true);

        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.DoesNotContain("온 곳과 같은 폴더", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }



    [Fact]
    public async Task AFolderCannotSwallowItsOwnDestination()
    {
        using var f = await ReadyAsync();

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var inside = Path.Combine(recipe, "Controls");
        Directory.CreateDirectory(inside);

        var items = ShellItems(recipe);

        Assert.False(f.ViewModel.CanDropOnto(recipe, items));
        Assert.False(f.ViewModel.CanDropOnto(inside, items));
    }





    [Fact]
    public async Task OneSwallowingFolderRefusesTheWholeBunch()
    {
        using var f = await ReadyAsync();

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var inside = Path.Combine(recipe, "Controls");
        Directory.CreateDirectory(inside);

        var items = ShellItems(recipe, Path.Combine(f.OutputPath, "a.dll"));

        Assert.Empty(MainViewModel.AcceptedForDrop(inside, items));
        Assert.False(f.ViewModel.CanDropOnto(inside, items));
    }







    [Fact]
    public async Task NothingIsAcceptedWhileAnotherOperationIsRunning()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.Rotating.SelectedItems = [.. f.Rotating.Items.Where(i => i.Name == "logs")];
        f.ViewModel.AddToTray(f.Row("문서"));

        var items = ShellItems(Path.Combine(f.OutputPath, "a.dll"));
        bool? whileRunning = null;

        void JudgeWhileRunning(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                whileRunning ??= f.ViewModel.CanDropOnto(f.DocsPath, items);
            }
        }

        f.ViewModel.PropertyChanged += JudgeWhileRunning;
        try
        {
            await f.ViewModel.CopySelectionAsync();
        }
        finally
        {
            f.ViewModel.PropertyChanged -= JudgeWhileRunning;
        }

        Assert.False(whileRunning);


        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }





    [Fact]
    public async Task NothingIsAcceptedWhileTheLayoutIsBeingEdited()
    {
        using var f = await ReadyAsync();
        var items = ShellItems(Path.Combine(f.WorkPath, "Main.cs"));

        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));

        f.ViewModel.ToggleLayoutEdit();
        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, items));


        await f.ViewModel.DropOntoPathAsync(f.DocsPath, items, copy: false);
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        f.ViewModel.ToggleLayoutEdit();
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }



    [Fact]
    public async Task AnEmptyShellDropIsNotAccepted()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, []));
        Assert.False(f.ViewModel.CanDropOnto(null, ShellItems(Path.Combine(f.WorkPath, "Main.cs"))));
        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, null));
    }





    [Fact]
    public async Task AnUnreachableDestinationIsRefusedWhenItIsActuallyDroppedOn()
    {
        using var f = await ReadyAsync();
        var items = ShellItems(Path.Combine(f.WorkPath, "Main.cs"));

        Assert.True(f.ViewModel.CanDropOnto(MainWindowFixture.UnreachablePath, items));

        await f.ViewModel.DropOntoPathAsync(MainWindowFixture.UnreachablePath, items, copy: false);

        Assert.Contains("목적지에 닿지 못했다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);


        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }



    [Fact]
    public async Task AFolderDraggedInFromTheShellIsCopiedWhole()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath, ShellItems(Path.Combine(f.WorkPath, "Recipe")), copy: true);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));


        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }



    [Fact]
    public async Task ATileThatWentIntoASubfolderAcceptsIntoThatSubfolder()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Equal(f.WorkPath, f.Rotating.ShellDropDestination);


        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(recipe, f.Rotating.ShellDropDestination);
        Assert.NotEqual(f.Rotating.Entry!.Path, f.Rotating.ShellDropDestination);

        await f.ViewModel.DropOntoPathAsync(
            f.Rotating.ShellDropDestination!,
            ShellItems(Path.Combine(f.OutputPath, "a.dll")),
            copy: true);

        Assert.True(File.Exists(Path.Combine(recipe, "a.dll")));
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "a.dll")));
    }


    [Fact]
    public void AnEmptyTileIsNotADropTarget()
    {
        using var f = new MainWindowFixture();

        Assert.Null(f.Rotating.ShellDropDestination);
    }




    [Fact]
    public async Task ATileShowingAnUnreachableFolderIsNotADropTarget()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        Assert.True(f.Rotating.HasFailure);
        Assert.Null(f.Rotating.ShellDropDestination);
    }


    [Fact]
    public void EveryTilePanelHasTheShellDropHostWired()
    {
        using var f = new MainWindowFixture();

        Assert.All(f.ViewModel.Panels, p => Assert.Same(f.ViewModel, p.ShellDropHost));
    }






    [Fact]
    public void APrivateFormatDropTakesTheInternalRuleAndStopsThere()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(new FileDropPayload("C:\\x", [])));

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
    }





    [Fact]
    public void BothFormatsOnOneDropStillTakeTheInternalRule()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(new FileDropPayload("C:\\x", [])));
        data.SetData(DataFormats.FileDrop, new[] { "C:\\x\\a.txt" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
    }

    [Fact]
    public void AShellOnlyDropTakesTheShellRule()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { "C:\\x\\a.txt" });

        Assert.Equal(DropRoute.Shell, DropRouter.Route(data));
    }

    [Fact]
    public void ADropWithNeitherFormatIsNotRouted()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "그냥 글자");

        Assert.Equal(DropRoute.None, DropRouter.Route(data));
        Assert.Equal(DropRoute.None, DropRouter.Route(null));
    }



    [Fact]
    public void AShellDropCopiesByDefaultAndMovesWithShift()
    {
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.None));
        Assert.False(DropRouter.ShellDropCopies(
            DragDropKeyStates.ShiftKey));


        Assert.True(DropRouter.ShellDropCopies(
            DragDropKeyStates.ControlKey));
    }





    [Fact]
    public void TheEffectIsNarrowedToOneAndNeverLink()
    {
        Assert.Equal(
            DragDropEffects.Copy,
            DropRouter.ShellEffect(DragDropKeyStates.None));

        Assert.Equal(
            DragDropEffects.Move,
            DropRouter.ShellEffect(DragDropKeyStates.ShiftKey));
    }


    [Fact]
    public async Task ShellPathsBecomeOperationItemsWithNoConversionLayer()
    {
        using var f = await ReadyAsync();

        var data = new DataObject();
        data.SetData(
            DataFormats.FileDrop,
            new[] { Path.Combine(f.WorkPath, "Main.cs"), Path.Combine(f.WorkPath, "Recipe") });

        var items = DropRouter.ShellItems(data);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Path == Path.Combine(f.WorkPath, "Main.cs") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path == Path.Combine(f.WorkPath, "Recipe") && i.IsDirectory);
    }






    [Fact]
    public async Task AShellDropWithNoModifierLeavesTheOriginalWhereItWas()
    {
        using var f = await ReadyAsync();
        var source = Path.Combine(f.WorkPath, "Main.cs");

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(source),
            DropRouter.ShellDropCopies(DragDropKeyStates.None));

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }


    [Fact]
    public async Task AShellDropWithShiftMovesTheOriginal()
    {
        using var f = await ReadyAsync();
        var source = Path.Combine(f.WorkPath, "Main.cs");

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(source),
            DropRouter.ShellDropCopies(DragDropKeyStates.ShiftKey));

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }







    [Fact]
    public async Task AnInternalDragIntoItsOwnFolderIsStillRefused()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.Rotating.SelectedItems = [.. f.Rotating.Items.Where(i => i.Name == "Main.cs")];

        var payload = f.Rotating.BuildDragPayload()!;

        Assert.False(f.ViewModel.CanDropOnto(f.Row("작업").Entry, payload));
        Assert.False(f.ViewModel.CanDropOnto(f.WorkPath, payload.Items));
    }








    [Fact]
    public async Task RecursiveSearchResultsAreJudgedByTheirOwnParentNotTheTilePath()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var panel = f.Rotating;
        panel.IncludeSubfolders = true;
        panel.SearchText = "NumberBox.xaml";

        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count == 0); i++)
        {
            await Task.Delay(25);
        }

        panel.SelectedItems = [.. panel.DisplayItems.Where(i => i.Name == "NumberBox.xaml")];
        var payload = panel.BuildDragPayload()!;


        Assert.Equal(f.WorkPath, payload.SourceFolder);
        Assert.Equal(
            Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml"),
            payload.Items.Single().Path);


        Assert.True(f.ViewModel.CanDropOnto(f.WorkPath, payload.Items));
        Assert.Single(MainViewModel.AcceptedForDrop(f.WorkPath, payload.Items));
    }

    private static void FillTree(string root, int count)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(root, $"f{i}.log"), new string('x', 64));
        }
    }
}
