using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;








public sealed class RenameTests
{


    [Fact]
    public async Task RenamesAFolderOnDisk()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe2")));


        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Recipe2", "Recipe.cs")));
    }

    [Fact]
    public async Task RenamesAFileOnDisk()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Program.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Program.cs")));
    }


    [Fact]
    public async Task TheListShowsTheNewName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Contains(panel.DisplayItems, i => i.Name == "Recipe2");
        Assert.DoesNotContain(panel.DisplayItems, i => i.Name == "Recipe");
    }


    [Fact]
    public async Task TheRenamedItemStaysSelected()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var selected = Assert.Single(panel.SelectedItems);
        Assert.Equal("Recipe2", selected.Name);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe2"), selected.FullPath);
    }

    [Fact]
    public async Task SaysWhatItRenamed()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Contains("Recipe", f.ViewModel.Message);
        Assert.Contains("Recipe2", f.ViewModel.Message);
    }


    [Fact]
    public async Task TheDialogIsPrefilledWithTheCurrentName()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Equal("Main.cs", Assert.Single(f.Prompt.NamesAsked));
    }






    [Fact]
    public async Task AcceptButtonTextIsStillChange()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal("바꾸기", Assert.Single(f.Prompt.AcceptButtonTextsAsked));
    }


    [Fact]
    public async Task ChangingOnlyTheCaseIsAllowed()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "RECIPE";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Equal(
            "RECIPE",
            Path.GetFileName(Directory.GetDirectories(f.WorkPath).Single(d => d.EndsWith("ECIPE"))));
    }




    [Fact]
    public async Task CancelChangesNothing()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Null(f.ViewModel.Message);
    }


    [Fact]
    public async Task TheSameNameIsANoOp()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Null(f.ViewModel.Message);
    }



    [Fact]
    public async Task RejectsANameThatAlreadyExists()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Views";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("같은 이름이 이미 있다", f.ViewModel.Message);
    }







    [Fact]
    public async Task RejectsAnEmptyName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "   ";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("빈 이름", f.ViewModel.Message);
    }

    [Fact]
    public async Task RejectsForbiddenCharacters()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = @"a\b";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("쓸 수 없는 문자", f.ViewModel.Message);
    }

    [Fact]
    public async Task RejectsWindowsReservedNames()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Aux.txt";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("예약 이름", f.ViewModel.Message);
    }


    [Fact]
    public async Task RejectsATrailingDot()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe.";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Contains("점이나 공백", f.ViewModel.Message);
    }




    [Fact]
    public async Task SurfacesAFileSystemFailure()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Locked.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var target = Path.Combine(f.WorkPath, "Main.cs");
        using (var hold = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await panel.RenameItemCommand.ExecuteAsync(
                panel.DisplayItems.Single(i => i.Name == "Main.cs"));
        }

        Assert.True(File.Exists(target));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("이름을 바꾸지 못했다", f.ViewModel.Message);


        Assert.False(f.ViewModel.MessageIsTransient);
    }



    [Fact]
    public async Task F2RenamesTheSingleSelectedItem()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SelectedItems = [panel.DisplayItems.Single(i => i.Name == "Recipe")];
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe2")));
        Assert.Equal("Recipe", Assert.Single(f.Prompt.NamesAsked));
    }

    [Fact]
    public async Task F2WithNothingSelectedIsSilent()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
        Assert.Null(f.ViewModel.Message);
    }


    [Fact]
    public async Task F2WithManySelectedIsRejected()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SelectedItems = [.. panel.DisplayItems.Take(2)];
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("하나만", f.ViewModel.Message);


        Assert.True(f.ViewModel.MessageIsTransient);
    }



    [Fact]
    public async Task NullAndEmptyTileAreIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(null);
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
    }


    [Fact]
    public async Task WorksOnAPinnedTileToo()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Views2";
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Tiles
            .Single(t => t.IsPinned && t.Panel.Entry is not null && t.Panel.Entry.Path == f.CodePath)
            .Panel;

        await pinned.RenameItemCommand.ExecuteAsync(
            pinned.DisplayItems.Single(i => i.Name == "Views"));

        Assert.True(Directory.Exists(Path.Combine(f.CodePath, "Views2")));
        Assert.Contains(pinned.DisplayItems, i => i.Name == "Views2");
    }
}




public sealed class RenameAnchorTests
{

    private static async Task<FolderPanelViewModel> AtTheParentOfWorkAsync(MainWindowFixture f)
    {
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.GoUpAsync();
        return panel;
    }


    private static async Task<Workspace> ReloadAsync(
        MainWindowFixture f, Guid id, Func<Workspace, bool> ready)
    {
        f.Store.Flush();

        Workspace? last = null;

        for (var i = 0; i < 40; i++)
        {
            if (f.Store.LoadWorkspace(id).Value is { } workspace)
            {
                last = workspace;
                if (ready(workspace))
                {
                    return workspace;
                }
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(
            last is null ? "저장 파일이 안 나타났다" : "저장 파일이 바뀐 값을 안 담았다");
    }



    [Fact]
    public async Task MovesTheRegistrationPathAndKeepsTheFolderId()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);


        Assert.Equal(id, f.Workspace.Folders.Single(e => e.Id == id).Id);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }


    [Fact]
    public async Task BothLayersMove()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, f.ViewModel.Rows.Single(r => r.Entry.Id == id).PathText);
    }


    [Fact]
    public async Task SurvivesAReload()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        var reloaded = await ReloadAsync(
            f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);

        Assert.Equal(expected, reloaded.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(5, reloaded.Folders.Count);
    }


    [Fact]
    public async Task CurrentPathNeverReachesTheFile()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        await ReloadAsync(f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);
        var json = await File.ReadAllTextAsync(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("currentPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("work2", json);
    }



    [Fact]
    public async Task ConfirmingAlsoRenamesTheDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("work2", f.Workspace.Folders.Single(e => e.Id == id).DisplayName);


        Assert.Single(f.Prompt.Messages);
    }





    [Fact]
    public async Task CancelingRefusesOnlyTheDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = false;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("작업", f.Workspace.Folders.Single(e => e.Id == id).DisplayName);


        Assert.True(Directory.Exists(expected));
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
    }


    [Fact]
    public async Task DescriptionIsNeverTouched()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("탐색용", f.Workspace.Folders.Single(e => e.Id == id).Description);
        Assert.Empty(f.FolderEditor.Opened);
    }


    [Fact]
    public async Task TheTileHeaderFollows()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("work2", panel.AnchorName);
    }



    [Fact]
    public async Task ANonAnchorFolderNeverAsksAndNeverTouchesFolders()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var before = f.Workspace.Folders.Select(e => e.Path).ToList();

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));


        Assert.Empty(f.Prompt.Messages);
        Assert.Equal(before, f.Workspace.Folders.Select(e => e.Path));
    }


    [Fact]
    public async Task AFileNeverAsks()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Program.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Empty(f.Prompt.Messages);
    }


    [Fact]
    public async Task WorksOnAnotherTilesAnchor()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "code2";
        f.Prompt.Answer = false;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("code").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.CodePath)!, "code2");


        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "code"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
    }
}
