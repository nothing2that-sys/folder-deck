using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>임시파일 → 원자적 교체 (§5, 완료 기준: 저장 중 강제 종료를 흉내 내도 기존 JSON 이 깨지지 않는다).</summary>
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

    /// <summary>
    /// 교체 직전에 죽은 상황 = <c>.tmp</c> 에 반쯤 쓰인 내용이 남고 대상은 그대로.
    /// 이때 대상 JSON 은 멀쩡해야 하고, 목록 열거도 <c>.tmp</c> 를 워크스페이스로 착각하면 안 된다.
    /// </summary>
    [Fact]
    public void CrashBeforeReplaceLeavesTheExistingFileIntact()
    {
        using var temp = new TempStore();
        var workspace = new Workspace { Title = "원본", Folders = [new FolderEntry { Path = @"D:\keep" }] };
        temp.Store.SaveWorkspace(workspace);
        var target = temp.Paths.WorkspaceFile(workspace.Id);
        var before = File.ReadAllText(target);

        // 새 내용을 쓰다가 죽은 흔적.
        File.WriteAllText(target + AtomicFile.TempSuffix, "{ \"schemaVersion\": 1, \"title\": \"반쯤 쓰");

        Assert.Equal(before, File.ReadAllText(target));
        var reloaded = temp.Store.LoadWorkspace(workspace.Id).ValueOrThrow();
        Assert.Equal("원본", reloaded.Title);
        Assert.Equal(@"D:\keep", Assert.Single(reloaded.Folders).Path);

        var listing = temp.Store.ListWorkspaces();
        Assert.Single(listing.Workspaces);
        Assert.Empty(listing.Failures);
    }

    /// <summary>남아 있던 <c>.tmp</c> 는 다음 쓰기가 덮어쓰고, 교체 후 사라진다.</summary>
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
}
