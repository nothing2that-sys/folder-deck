using System.ComponentModel;
using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;
















public sealed class TileDropTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];


    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);




    private static DataObject Dragged(FolderPanelViewModel from)
    {
        var payload = from.BuildDragPayload()!;
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(payload));
        ShellExport.Attach(data, payload.Items);
        return data;
    }


    private static async Task<bool> DropOntoTileAsync(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys)
    {
        if (Accepted(f, tile, data, keys) is not { } incoming)
        {
            return false;
        }

        await f.ViewModel.DropOntoPathAsync(
            tile.ShellDropDestination!, incoming.Items, incoming.Copy);
        return true;
    }


    private static (IReadOnlyList<OperationItem> Items, bool Copy)? Accepted(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys) =>
        tile.ShellDropDestination is { } destination
        && DropRouter.Resolve(data, keys, destination) is { } resolved
        && f.ViewModel.CanDropOnto(destination, resolved.Items)
            ? resolved
            : null;

    private static bool Accepts(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys) =>
        Accepted(f, tile, data, keys) is not null;




    [Fact]
    public async Task DroppingFromOneTileOntoAnotherMovesByDefault()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.None));

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));


        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.DoesNotContain("앱 밖으로", f.ViewModel.Message);


        Assert.Empty(f.Prompt.Messages);
        Assert.Empty(f.Prompt.ConflictMessages);
    }


    [Fact]
    public async Task DroppingWithControlCopiesAndLeavesTheOriginal()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.ControlKey));

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }


    [Fact]
    public async Task AFolderAndSeveralItemsGoTogether()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs", "Recipe", "Views");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.None));

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Recipe", "Recipe.cs")));
        Assert.True(Directory.Exists(Path.Combine(f.OutputPath, "Views")));
        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }





    [Fact]
    public async Task TheDestinationIsWhatTheTileIsShowingNotItsAnchor()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");

        var views = Path.Combine(f.WorkPath, "Views");
        Assert.Equal(views, f.Rotating.ShellDropDestination);
        Assert.NotEqual(f.Rotating.Entry!.Path, f.Rotating.ShellDropDestination);

        Assert.True(await DropOntoTileAsync(f, f.Rotating, Dragged(code), DragDropKeyStates.None));

        Assert.True(File.Exists(Path.Combine(views, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }







    [Fact]
    public async Task DroppingBackIntoTheSameTileIsRefused()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs", "Recipe");

        Assert.False(Accepts(f, f.Rotating, Dragged(f.Rotating), DragDropKeyStates.None));
        Assert.False(Accepts(f, f.Rotating, Dragged(f.Rotating), DragDropKeyStates.ControlKey));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }





    [Fact]
    public async Task TwoTilesShowingTheSameFolderRefuseEachOther()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);


        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(f.CodePath);
        Assert.Equal(code.ShellDropDestination, f.Rotating.ShellDropDestination);

        Select(code, "Main.cs");
        var data = Dragged(code);

        Assert.False(Accepts(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }


    [Fact]
    public async Task AFolderCannotBeDroppedIntoATileShowingItsOwnSubfolder()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Recipe");
        var data = Dragged(code);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.CodePath, "Recipe"));

        Assert.False(Accepts(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(Directory.Exists(Path.Combine(f.CodePath, "Recipe")));


        Select(code, "Recipe", "Main.cs");
        Assert.False(Accepts(f, f.Rotating, Dragged(code), DragDropKeyStates.None));
    }


    [Fact]
    public async Task ATileDoesNotAcceptWhileAnotherOperationIsRunning()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        var data = Dragged(code);


        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "logs");
        f.ViewModel.AddToTray(f.Row("문서"));

        bool? whileRunning = null;

        void JudgeWhileRunning(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                whileRunning ??= Accepts(f, f.Rotating, data, DragDropKeyStates.None);
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


        Assert.True(await DropOntoTileAsync(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }


    [Fact]
    public async Task ATileDoesNotAcceptWhileTheLayoutIsBeingEdited()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        var data = Dragged(f.Rotating);
        var output = Tile(f, f.OutputPath);

        Assert.True(Accepts(f, output, data, DragDropKeyStates.None));

        f.ViewModel.ToggleLayoutEdit();
        Assert.False(Accepts(f, output, data, DragDropKeyStates.None));

        f.ViewModel.ToggleLayoutEdit();
        Assert.True(Accepts(f, output, data, DragDropKeyStates.None));
    }












    [Fact]
    public async Task RecursiveSearchResultsDroppedOntoTheirOwnTileAreGatheredUp()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();


        File.WriteAllText(Path.Combine(f.WorkPath, "NumberTop.cs"), "x");

        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var panel = f.Rotating;
        panel.IncludeSubfolders = true;
        panel.SearchText = "Number";

        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count < 4); i++)
        {
            await Task.Delay(25);
        }

        panel.SelectedItems = [.. panel.DisplayItems];

        var deep = Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml");
        Assert.True(File.Exists(deep));
        Assert.Contains(panel.SelectedItems, i => i.FullPath == deep);
        Assert.Contains(panel.SelectedItems, i => i.FullPath == Path.Combine(f.WorkPath, "NumberTop.cs"));

        Assert.True(await DropOntoTileAsync(f, panel, Dragged(panel), DragDropKeyStates.None));


        Assert.True(File.Exists(Path.Combine(f.WorkPath, "NumberBox.xaml")));
        Assert.False(File.Exists(deep));


        Assert.True(File.Exists(Path.Combine(f.WorkPath, "NumberTop.cs")));
        Assert.Contains("온 곳과 같은 폴더 1", f.ViewModel.Message);
    }








    [Fact]
    public async Task ADragThatCarriesBothFormatsTakesTheInternalRuleWhenItLandsInside()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        var data = Dragged(f.Rotating);

        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));


        var destination = f.OutputPath;


        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.None, destination)!.Value.Copy);


        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey, destination)!.Value.Copy);

        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.ControlKey, destination)!.Value.Copy);
    }










    [Theory]

    [InlineData(DragDropKeyStates.None, true, false)]
    [InlineData(DragDropKeyStates.None, false, true)]

    [InlineData(DragDropKeyStates.ControlKey, true, true)]
    [InlineData(DragDropKeyStates.ControlKey, false, true)]

    [InlineData(DragDropKeyStates.ShiftKey, true, false)]
    [InlineData(DragDropKeyStates.ShiftKey, false, false)]

    [InlineData(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey, true, true)]
    [InlineData(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey, false, true)]
    public void TheInternalRuleReadsControlShiftAndTheVolume(
        DragDropKeyStates keys, bool sameVolume, bool copies) =>
        Assert.Equal(copies, DropRouter.InternalDropCopies(keys, sameVolume));









    [Fact]
    public void TheShellRuleDidNotMove()
    {
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.None));
        Assert.False(DropRouter.ShellDropCopies(DragDropKeyStates.ShiftKey));
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.ControlKey));
        Assert.False(
            DropRouter.ShellDropCopies(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey));
    }











    [Fact]
    public void AnInternalDropCarriesItsItemsUnchanged()
    {
        var items = new[]
        {
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
        };

        var data = new DataObject();
        data.SetData(
            FolderPanelView.FileDragFormat,
            DragPayloadCodec.Pack(new FileDropPayload(@"C:\deck\work", items)));

        Assert.Equal(
            items, DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out")!.Value.Items);
    }








    [Fact]
    public void ABundleEntirelyOnTheDestinationVolumeCountsAsSameVolume() =>
        Assert.True(DropRouter.AllOnSameVolume(
            [
                new OperationItem(@"C:\deck\work\Main.cs", false),
                new OperationItem(@"C:\deck\other\Views", true),
            ],
            @"C:\deck\out"));





    [Fact]
    public void OneItemOnAnotherVolumeMakesTheWholeBundleCopy()
    {
        IReadOnlyList<OperationItem> mixed =
        [
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Recipe", true),
            new OperationItem(@"D:\elsewhere\one.txt", false),
        ];

        Assert.False(DropRouter.AllOnSameVolume(mixed, @"C:\deck\out"));


        Assert.True(DropRouter.InternalDropCopies(
            DragDropKeyStates.None, DropRouter.AllOnSameVolume(mixed, @"C:\deck\out")));
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnknownDestinationIsNotTheSameVolume(string? destination) =>
        Assert.False(DropRouter.AllOnSameVolume(
            [new OperationItem(@"C:\deck\work\Main.cs", false)], destination));







    [Fact]
    public void TheTargetCaptionSaysWhatTheDropWillDo()
    {
        Assert.Equal("여기로 복사", DropTargetCaption.For(copy: true));
        Assert.Equal("여기로 이동", DropTargetCaption.For(copy: false));
    }





    [Fact]
    public async Task TheThreeTargetsReadTheSameCaption()
    {
        using var f = await ReadyAsync();

        f.ViewModel.AddToTray(f.Row("산출물"));

        var row = f.Row("작업");
        var tray = f.ViewModel.Tray.Single();
        var tile = f.Rotating;

        foreach (var copy in new[] { false, true })
        {
            row.DropTargetCopies = copy;
            tray.DropTargetCopies = copy;
            tile.DropTargetCopies = copy;

            Assert.Equal(DropTargetCaption.For(copy), row.DropTargetText);
            Assert.Equal(DropTargetCaption.For(copy), tray.DropTargetText);
            Assert.Equal(DropTargetCaption.For(copy), tile.DropTargetText);
        }
    }





    [Fact]
    public async Task TheCaptionRaisesChangeWhenTheVerdictFlips()
    {
        using var f = await ReadyAsync();

        var row = f.Row("작업");
        var seen = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        row.DropTargetCopies = true;

        Assert.Contains(nameof(FolderRowViewModel.DropTargetText), seen);
        Assert.Equal("여기로 복사", row.DropTargetText);
    }





    [Fact]
    public void AShellOnlyDropStillTakesTheShellRule()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { @"C:\outside\a.txt" });


        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\outside")!.Value.Copy);
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey, @"C:\outside")!.Value.Copy);


        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out")!.Value.Copy);


        Assert.True(
            DropRouter.Resolve(data, DragDropKeyStates.ControlKey, @"C:\outside")!.Value.Copy);
    }

    [Fact]
    public void ADropWithNeitherFormatIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "그냥 글자");

        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out"));
        Assert.Null(DropRouter.Resolve(null, DragDropKeyStates.None, @"C:\deck\out"));
    }


    [Fact]
    public void APrivateFormatWithTheWrongPayloadIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, "뭉치가 아니다");
        data.SetData(DataFormats.FileDrop, new[] { @"C:\outside\a.txt" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out"));
    }


    [Fact]
    public void TheEffectIsOneOfTwoAndNeverLink()
    {
        Assert.Equal(DragDropEffects.Copy, DropRouter.Effect(copy: true));
        Assert.Equal(DragDropEffects.Move, DropRouter.Effect(copy: false));
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
