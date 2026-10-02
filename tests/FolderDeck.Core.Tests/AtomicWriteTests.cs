using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;


public sealed class AtomicWriteTests
{
    [Fact]
    public void SuccessfulWriteLeavesNoTempFileBehind()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "임시파일 정리" };

        temp.Store.SaveWorkspace(workspace);

        var target = temp.Paths.WorkspaceFile(workspace.Id);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(target + AtomicFile.TempSuffix));
    }





    [Fact]
    public void CrashBeforeReplaceLeavesTheExistingFileIntact()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "원본", Folders = [new FolderEntry { Path = @"D:\keep" }] };
        temp.Store.SaveWorkspace(workspace);
        var target = temp.Paths.WorkspaceFile(workspace.Id);
        var before = File.ReadAllText(target);


        File.WriteAllText(target + AtomicFile.TempSuffix, "{ \"schemaVersion\": 1, \"title\": \"반쯤 쓰");

        Assert.Equal(before, File.ReadAllText(target));
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();
        Assert.Equal("원본", reloaded.Title);
        Assert.Equal(@"D:\keep", Assert.Single(reloaded.Folders).Path);

        var listing = temp.Store.ListWorkspaces();
        Assert.Single(listing.Workspaces);
        Assert.Empty(listing.Failures);
    }


    [Fact]
    public void StaleTempFileIsOverwrittenByTheNextWrite()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "1차" };
        temp.Store.SaveWorkspace(workspace);
        var target = temp.Paths.WorkspaceFile(workspace.Id);
        File.WriteAllText(target + AtomicFile.TempSuffix, "쓰레기");

        workspace.Title = "2차";
        Assert.Null(temp.Store.SaveWorkspace(workspace));

        Assert.False(File.Exists(target + AtomicFile.TempSuffix));
        Assert.Equal("2차", temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow().Title);
    }

    [Fact]
    public void RepeatedWritesNeverLeaveAnUnparseableTarget()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "반복" };

        for (var i = 0; i < 50; i++)
        {
            workspace.Title = $"반복 {i}";
            workspace.Folders = [.. Enumerable.Range(0, i).Select(n => new FolderEntry { Path = $@"D:\p{n}" })];
            Assert.Null(temp.Store.SaveWorkspace(workspace));

            var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();
            Assert.Equal($"반복 {i}", reloaded.Title);
            Assert.Equal(i, reloaded.Folders.Count);
        }
    }

    [Fact]
    public void AtomicWriteCreatesMissingDirectories()
    {
        using var temp = new TempStore();
        var nested = Path.Combine(temp.Root, "a", "b", "c.json");

        AtomicFile.WriteAllText(nested, "{}");

        Assert.Equal("{}", File.ReadAllText(nested));
    }

    [Fact]
    public void FilesAreWrittenAsUtf8WithoutBom()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "한글 제목" };

        temp.Store.SaveWorkspace(workspace);
        var bytes = File.ReadAllBytes(temp.Paths.WorkspaceFile(workspace.Id));

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("한글 제목", File.ReadAllText(temp.Paths.WorkspaceFile(workspace.Id)));
    }





    [Fact]
    public void ConcurrentWritesToTheSameFileAllSucceed()
    {
        using var temp = new TempStore();
        var target = Path.Combine(temp.Paths.WorkspacesDir, "race.json");
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 40, i =>
        {
            try
            {
                AtomicFile.WriteAllText(target, $$"""{ "n": {{i}} }""");
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        Assert.Empty(failures);
        Assert.StartsWith("{", File.ReadAllText(target));


        Assert.Empty(Directory.GetFiles(temp.Paths.WorkspacesDir, "*" + AtomicFile.TempSuffix));
    }
}
