using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>테스트 워크스페이스 JSON 로드 검증 (완료 기준: "테스트 JSON 을 로드하면 모델이 정확히 채워진다").</summary>
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
        Assert.Equal("EIS Item 개발", workspace.Title);
        Assert.Contains("Phase 1", workspace.Description);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.FromHours(9)),
            workspace.LastUsed);
        Assert.Equal(6, workspace.Folders.Count);

        var code = workspace.Folders[0];
        Assert.Equal(Guid.Parse("a1000000-0000-4000-8000-000000000001"), code.Id);
        Assert.Equal(@"C:\FolderDeckExample\Source", code.Path);
        Assert.Equal("basic-items", code.DisplayName);
        Assert.Equal("기본 아이템 구현 코드", code.Description);
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

        // 접근 불가 확인용 UNC 경로도 그대로 살아 있어야 한다 (§5 — 목록에서 지우지 않는다).
        Assert.Equal(@"\\nas-not-connected\logs", workspace.Folders[5].Path);
    }

    [Fact]
    public void SampleLeavesPhase2SlotsNull()
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
    public void SampleFolderPathsExistExceptTheDeliberatelyUnreachableOne()
    {
        using var temp = new TempStore();
        File.Copy(SamplePath, temp.Paths.WorkspaceFile(SampleId));

        var workspace = temp.Store.LoadWorkspace(SampleId).ValueOrThrow();
        var reachable = workspace.Folders.Where(f => !f.Path.StartsWith(@"\\", StringComparison.Ordinal));

        foreach (var folder in reachable)
        {
            Assert.True(Directory.Exists(folder.Path), $"실제로 있는 폴더여야 한다: {folder.Path}");
        }
    }
}
