using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

public sealed class CorruptDataTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [Fact]
    public void TruncatedJsonFailsAsCorruptData()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", "{ \"schemaVersion\": 1, \"id\": \"" + Id + "\", \"title\": \"잘린 파일");

        var result = temp.Store.LoadWorkspace(Id);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(StorageFailureKind.CorruptData, result.Failure!.Kind);
        Assert.Throws<FolderDeckStorageException>(() => result.ValueOrThrow());
    }

    [Fact]
    public void GarbageFailsAsCorruptData()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", "이건 JSON 이 아니다");

        Assert.Equal(StorageFailureKind.CorruptData, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void JsonNullFailsInsteadOfProducingAnEmptyWorkspace()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", "null");

        var result = temp.Store.LoadWorkspace(Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(StorageFailureKind.CorruptData, result.Failure!.Kind);
    }

    [Fact]
    public void UnknownEnumValueFails()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "title": "이상한 enum",
              "folders": [ { "id": "a1000000-0000-4000-8000-000000000001",
                             "path": "D:\\x", "viewMode": "tiles" } ]
            }
            """);

        Assert.Equal(StorageFailureKind.CorruptData, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void UnknownTileKindFailsAsCorruptData()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "title": "모르는 타일 종류",
              "folders": [],
              "tiles": [ { "kind": "bogus", "cellX": 0, "cellY": 0 } ]
            }
            """);

        Assert.Equal(StorageFailureKind.CorruptData, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void NumericEnumValueFails()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "title": "숫자 enum",
              "folders": [ { "id": "a1000000-0000-4000-8000-000000000001",
                             "path": "D:\\x", "sortBy": 2 } ]
            }
            """);

        Assert.Equal(StorageFailureKind.CorruptData, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void FutureSchemaVersionFailsAsUnsupported()
    {
        using var temp = new TempStore();
        var future = Workspace.CurrentSchemaVersion + 1;
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            { "schemaVersion": {{future}}, "id": "{{Id}}", "title": "미래 파일", "folders": [] }
            """);

        var result = temp.Store.LoadWorkspace(Id);

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, result.Failure!.Kind);
        Assert.Contains(future.ToString(), result.Failure.Message);
    }

    [Fact]
    public void MissingSchemaVersionFailsAsInvalidContent()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            { "id": "{{Id}}", "title": "버전 없음", "folders": [] }
            """);

        Assert.Equal(StorageFailureKind.InvalidContent, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void MissingIdFailsAsInvalidContent()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", """
            { "schemaVersion": 1, "title": "id 없음", "folders": [] }
            """);

        Assert.Equal(StorageFailureKind.InvalidContent, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void FileNameNotMatchingIdFailsAsInvalidContent()
    {
        using var temp = new TempStore();
        var path = temp.WriteRawWorkspace("copy-of-workspace.json", $$"""
            { "schemaVersion": 1, "id": "{{Id}}", "title": "복사본", "folders": [] }
            """);

        var result = temp.Store.LoadWorkspaceFile(path);

        Assert.Equal(StorageFailureKind.InvalidContent, result.Failure!.Kind);
        Assert.Contains($"{Id}.json", result.Failure.Message);
    }

    [Fact]
    public void FolderWithoutPathFailsAsInvalidContent()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "title": "앵커 없는 폴더",
              "folders": [ { "id": "a1000000-0000-4000-8000-000000000001", "path": "  " } ]
            }
            """);

        Assert.Equal(StorageFailureKind.InvalidContent, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void DuplicateFolderIdFailsAsInvalidContent()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "title": "중복 id",
              "folders": [
                { "id": "a1000000-0000-4000-8000-000000000001", "path": "D:\\a" },
                { "id": "a1000000-0000-4000-8000-000000000001", "path": "D:\\b" }
              ]
            }
            """);

        Assert.Equal(StorageFailureKind.InvalidContent, temp.Store.LoadWorkspace(Id).Failure!.Kind);
    }

    [Fact]
    public void MissingFileFailsAsNotFoundNotCorrupt()
    {
        using var temp = new TempStore();

        var result = temp.Store.LoadWorkspace(Guid.NewGuid());

        Assert.Equal(StorageFailureKind.NotFound, result.Failure!.Kind);
    }

    [Fact]
    public void ListingReportsFailuresAlongsideGoodWorkspaces()
    {
        using var temp = new TempStore();
        var good = new Workspace { Title = "정상" };
        temp.Store.SaveWorkspace(good);
        temp.WriteRawWorkspace($"{Id}.json", "{ 부서진");

        var listing = temp.Store.ListWorkspaces();

        Assert.Equal(good.Id, Assert.Single(listing.Workspaces).Id);
        var failure = Assert.Single(listing.Failures);
        Assert.Equal(StorageFailureKind.CorruptData, failure.Kind);
        Assert.Contains($"{Id}.json", failure.Path);
    }
}
