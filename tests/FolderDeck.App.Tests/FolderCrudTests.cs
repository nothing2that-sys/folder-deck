using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;








public sealed class FolderCrudTests
{

    private static FileItemViewModel FolderItemAt(string fullPath) =>
        new(new FolderItem(
            Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
            fullPath,
            IsDirectory: true,
            Size: null,
            ModifiedUtc: DateTime.UtcNow));



    [Fact]
    public void RegisteringAddsToBothFoldersAndRows()
    {
        using var f = new MainWindowFixture();
        var target = Path.Combine(f.CodePath, "Recipe");

        f.ViewModel.RegisterFolder(FolderItemAt(target));


        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Equal(6, f.ViewModel.Rows.Count);


        var added = f.Workspace.Folders[^1];
        Assert.Equal(target, added.Path);
        Assert.Same(added, f.ViewModel.Rows[^1].Entry);


        Assert.Equal("Recipe", added.DisplayName);
        Assert.Null(added.Description);
    }



    [Fact]
    public void RegisteringAPathThatIsAlreadyRegisteredIsRejected()
    {
        using var f = new MainWindowFixture();


        f.ViewModel.RegisterFolder(FolderItemAt(f.DocsPath + Path.DirectorySeparatorChar));

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);


        Assert.Contains("이미 등록된 폴더다", f.ViewModel.Message!);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public void FolderListAddRegistersSelectionAndSkipsDuplicates()
    {
        using var f = new MainWindowFixture();
        var target = Path.Combine(f.CodePath, "Recipe");

        f.ViewModel.AddFolders([target, f.DocsPath]);

        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Equal(6, f.ViewModel.Rows.Count);
        Assert.Equal(target, f.Workspace.Folders[^1].Path);
        Assert.Contains("폴더 1개를 등록했다", f.ViewModel.Message!);
        Assert.Contains("이미 등록 1개 제외", f.ViewModel.Message!);
    }

    [Fact]
    public void EmptyFolderSelectionDoesNotChangeRegistration()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.AddFolders([]);

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);
    }





    [Fact]
    public void TheMainWindowTakesTheFolderPicker()
    {
        var parameters = Assert.Single(typeof(MainWindow).GetConstructors()).GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(MainViewModel), parameters[0].ParameterType);
        Assert.Equal(typeof(IFolderPicker), parameters[1].ParameterType);
    }



    [Fact]
    public void RemovingTakesItOutOfBothFoldersAndRows()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;

        f.ViewModel.RemoveFolder(f.Row("문서"));

        Assert.Equal(4, f.Workspace.Folders.Count);
        Assert.Equal(4, f.ViewModel.Rows.Count);
        Assert.DoesNotContain(f.Workspace.Folders, e => e.DisplayName == "문서");
        Assert.DoesNotContain(f.ViewModel.Rows, r => r.DisplayName == "문서");
    }



    [Fact]
    public void RemovingCleansTilesTrayAndMacrosAndSaysHowMany()
    {
        using var f = new MainWindowFixture(seedFolderReferences: true);
        var row = f.Row("code");
        var id = row.Entry.Id;


        Assert.Contains(f.Workspace.Tiles!, t => t.Kind == TileKind.Pinned && t.FolderId == id);
        Assert.Single(f.ViewModel.Tray);
        Assert.Equal(id, Assert.Single(f.Workspace.Macros![0].Dest!.FolderIds!));

        f.Prompt.Answer = true;
        f.ViewModel.RemoveFolder(row);


        Assert.Contains("타일 1개 · 대상함 1개 · 매크로 1개", Assert.Single(f.Prompt.Messages));

        Assert.DoesNotContain(f.Workspace.Tiles!, t => t.FolderId == id);
        Assert.Empty(f.ViewModel.Tray);
        Assert.Null(f.Workspace.CopyTray);


        Assert.Null(f.Workspace.Macros![0].Dest!.FolderIds);
    }



    [Fact]
    public void CancellingRemovalChangesNothing()
    {
        using var f = new MainWindowFixture(seedFolderReferences: true);
        var row = f.Row("code");
        var id = row.Entry.Id;
        var tileCount = f.Workspace.Tiles!.Count;

        f.Prompt.Answer = false;
        f.ViewModel.RemoveFolder(row);


        Assert.Single(f.Prompt.Messages);

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(tileCount, f.Workspace.Tiles!.Count);
        Assert.Contains(f.Workspace.Tiles!, t => t.Kind == TileKind.Pinned && t.FolderId == id);
        Assert.Equal(id, Assert.Single(f.Workspace.CopyTray!));
        Assert.Equal(id, Assert.Single(f.Workspace.Macros![0].Dest!.FolderIds!));
        Assert.Single(f.ViewModel.Tray);
    }



    [Fact]
    public void EditingChangesNameAndDescriptionAndLeavesPinnedAlone()
    {
        using var f = new MainWindowFixture();
        var row = f.Row("code");


        Assert.True(row.Entry.Pinned);
        var viewMode = row.Entry.ViewMode;
        var sortBy = row.Entry.SortBy;
        var rotationSlot = row.Entry.RotationSlot;

        f.FolderEditor.Answer = new FolderEditDraft("코드 저장소", "설명이 처음 들어간다");
        f.ViewModel.EditFolder(row);


        var opened = Assert.Single(f.FolderEditor.Opened);
        Assert.Equal("code", opened.DisplayName);
        Assert.Equal(f.CodePath, f.FolderEditor.OpenedPath);

        Assert.Equal("코드 저장소", row.Entry.DisplayName);
        Assert.Equal("설명이 처음 들어간다", row.Entry.Description);


        Assert.Equal("코드 저장소", row.DisplayName);
        Assert.True(row.HasDescription);


        Assert.True(row.Entry.Pinned);
        Assert.Equal(viewMode, row.Entry.ViewMode);
        Assert.Equal(sortBy, row.Entry.SortBy);
        Assert.Equal(rotationSlot, row.Entry.RotationSlot);
    }



    [Fact]
    public void SchemaVersionStaysWhereItWasThroughAllThree()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        f.FolderEditor.Answer = new FolderEditDraft("문서들", "사양 모음");

        f.ViewModel.RegisterFolder(FolderItemAt(Path.Combine(f.CodePath, "Views")));
        f.ViewModel.EditFolder(f.Row("문서"));
        f.ViewModel.RemoveFolder(f.Row("작업"));
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        Assert.Equal(Workspace.CurrentSchemaVersion, reopened.SchemaVersion);


        Assert.Equal(5, reopened.Folders.Count);
        Assert.Contains(reopened.Folders, e => e.DisplayName == "Views");
        Assert.Contains(reopened.Folders, e => e.Description == "사양 모음");
        Assert.DoesNotContain(reopened.Folders, e => e.DisplayName == "작업");
    }
}
