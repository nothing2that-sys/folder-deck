using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

public sealed class SampleWorkspaceTests
{
    private static readonly Guid SampleId = Guid.Parse("2f4a6b18-9c31-4d7e-8a05-1b6c9de24f70");

    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "samples", "workspaces", $"{SampleId}.json");

    [Fact]
    public void SampleFileIsCopiedNextToTests()
    {
        Assert.True(File.Exists(SamplePath), $"테스트 워크스페이스 JSON 이 없다: {SamplePath}");
    }

    [Fact]
    public void SampleLoadsWithEveryFieldPopulated()
    {
        using var temp = new TempStore();
        File.Copy(SamplePath, temp.Paths.WorkspaceFile(SampleId));

        var workspace = temp.Store.LoadWorkspace(SampleId).ValueOrThrow();

        Assert.Equal(1, workspace.SchemaVersion);
        Assert.Equal(SampleId, workspace.Id);
        Assert.Equal("예제 작업 공간", workspace.Title);
        Assert.Contains("예제 폴더", workspace.Description);
        Assert.Equal(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            workspace.LastUsed);
        Assert.Equal(6, workspace.Folders.Count);

        var code = workspace.Folders[0];
        Assert.Equal(Guid.Parse("a1000000-0000-4000-8000-000000000001"), code.Id);
        Assert.Equal(@"C:\FolderDeckExample\Source", code.Path);
        Assert.Equal("소스", code.DisplayName);
        Assert.Equal("소스 예제", code.Description);
        Assert.Equal(FolderViewMode.Details, code.ViewMode);
        Assert.Equal(SortBy.Name, code.SortBy);
        Assert.False(code.SortDesc);
        Assert.True(code.Pinned);

        var docs = workspace.Folders[3];
        Assert.Equal(FolderViewMode.List, docs.ViewMode);
        Assert.False(docs.Pinned);

        var outputs = workspace.Folders[2];
        Assert.Equal(SortBy.Modified, outputs.SortBy);
        Assert.True(outputs.SortDesc);

        Assert.Equal(@"\\example-server\logs", workspace.Folders[5].Path);
    }

    [Fact]
    public void LegacySampleLeavesOptionalSlotsNull()
    {
        using var temp = new TempStore();
        File.Copy(SamplePath, temp.Paths.WorkspaceFile(SampleId));

        var workspace = temp.Store.LoadWorkspace(SampleId).ValueOrThrow();

        Assert.Null(workspace.Grid);
        Assert.Null(workspace.Tiles);
        Assert.Null(workspace.Window);
        Assert.Null(workspace.CopyTray);
        Assert.Null(workspace.Macros);
    }

    [Fact]
    public void SampleUsesGenericPathsWithoutRequiringLocalFolders()
    {
        using var temp = new TempStore();
        File.Copy(SamplePath, temp.Paths.WorkspaceFile(SampleId));
        var workspace = temp.Store.LoadWorkspace(SampleId).ValueOrThrow();
        Assert.All(workspace.Folders.Take(5), folder =>
            Assert.StartsWith(@"C:\FolderDeckExample\", folder.Path));
        Assert.Equal(@"\\example-server\logs", workspace.Folders[5].Path);
    }
}