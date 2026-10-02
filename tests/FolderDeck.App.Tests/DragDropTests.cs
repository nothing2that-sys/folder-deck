using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

public sealed class DragDropTests
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
    public async Task ThereIsNothingToDragWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.Null(f.ViewModel.Panels[0].BuildDragPayload());
    }

    [Fact]
    public async Task ThePayloadCarriesTheSelectionAndTheFolderItCameFrom()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        var payload = code.BuildDragPayload()!;

        Assert.Equal(f.CodePath, payload.SourceFolder);
        Assert.Equal(2, payload.Items.Count);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Main.cs") && !i.IsDirectory);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Recipe") && i.IsDirectory);
    }

    private static FileItemViewModel FakeItem(string fullPath, bool isDirectory = false) =>
        new(new FolderItem(Path.GetFileName(fullPath), fullPath, isDirectory, Size: 0, ModifiedUtc: DateTime.UtcNow));

    [Fact]
    public async Task PressingAnItemInsideTheSelectionCarriesTheWholeSelection()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");
        var pressed = code.Items.Single(i => i.Name == "Main.cs");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.Equal(2, payload.Items.Count);
    }

    [Fact]
    public async Task PressingAnItemOutsideTheSelectionCarriesOnlyThatItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs");
        var pressed = code.Items.Single(i => i.Name == "Recipe");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.Equal(f.CodePath, payload.SourceFolder);
        Assert.Single(payload.Items);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Recipe") && i.IsDirectory);

        Select(code, "Main.cs", "Recipe");
        var mainCs = code.Items.Single(i => i.Name == "Main.cs");
        var pressedDifferentCase = FakeItem(mainCs.FullPath.ToUpperInvariant());

        var casePayload = code.BuildDragPayload(pressedDifferentCase)!;

        Assert.Equal(2, casePayload.Items.Count);
    }

    [Fact]
    public async Task NoPressedItemMeansTheWholeSelectionAsBefore()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        var payload = code.BuildDragPayload()!;

        Assert.Equal(2, payload.Items.Count);
    }

    [Fact]
    public async Task PressingAnItemWithNoSelectionAtAllStillCarriesThatOneItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        var pressed = code.Items.Single(i => i.Name == "Main.cs");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.NotNull(payload);
        Assert.Single(payload.Items);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Main.cs") && !i.IsDirectory);
    }

    [Fact]
    public async Task NoCurrentFolderMeansNoPayloadEvenWithAPressedItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        var pressed = code.Items.Single(i => i.Name == "Main.cs");
        code.Clear();

        Assert.Null(code.BuildDragPayload(pressed));
    }

    [Fact]
    public async Task DroppingMovesByDefault()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        Assert.Empty(f.Prompt.Messages);
        Assert.Contains("성공 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task DroppingWithControlCopies()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: true);

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task ManyItemsGoInOneDrop()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll", "b.dll");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.ViewModel.Panels[1].BuildDragPayload()!, copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "a.dll")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "b.dll")));
        Assert.Empty(Directory.GetFiles(f.OutputPath));
    }

    [Fact]
    public async Task AFolderIsDroppedWholeAndRecursively()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Recipe");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));
        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }

    [Fact]
    public async Task DroppingIntoTheFolderItCameFromDoesNothing()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        var payload = f.Rotating.BuildDragPayload()!;

        Assert.False(f.ViewModel.CanDropOnto(f.Row("작업").Entry, payload));

        await f.ViewModel.DropOntoFolderAsync(f.Row("작업").Entry, payload, copy: false);

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.Null(f.ViewModel.Message);
    }

    [Fact]
    public async Task AFolderCannotBeDroppedOntoItself()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Recipe");
        var payload = f.Rotating.BuildDragPayload()!;

        var recipe = new Core.Models.FolderEntry { Path = Path.Combine(f.WorkPath, "Recipe") };
        var inside = new Core.Models.FolderEntry
        {
            Path = Path.Combine(f.WorkPath, "Recipe", "Controls"),
        };

        Assert.False(f.ViewModel.CanDropOnto(recipe, payload));
        Assert.False(f.ViewModel.CanDropOnto(inside, payload));
    }

    [Fact]
    public async Task AnEmptyPayloadIsNotAccepted()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanDropOnto(f.Row("문서").Entry, null));
        Assert.False(f.ViewModel.CanDropOnto(f.Row("문서").Entry, new FileDropPayload(f.WorkPath, [])));
        Assert.False(f.ViewModel.CanDropOnto(null, new FileDropPayload(f.WorkPath, [])));
    }

    [Fact]
    public async Task DroppingOnATrayEntryTargetsThatEntryAlone()
    {
        using var f = await ReadyAsync();
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        var docs = f.ViewModel.Tray.Single(t => t.DisplayName == "문서");
        await f.ViewModel.DropOntoFolderAsync(docs.Entry, f.Rotating.BuildDragPayload()!, copy: true);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task DroppingOnAnUnreachableFolderIsRefusedWithAReasonThatStays()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("설비 로그").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.Contains("목적지에 닿지 못했다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    [Fact]
    public async Task ListsRefreshAfterADrop()
    {
        using var f = await ReadyAsync();
        var docsTile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(docsTile.Panel.Items);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.ViewModel.Panels[0].BuildDragPayload()!, copy: true);

        Assert.Equal(2, docsTile.Panel.Items.Count);
    }
}
