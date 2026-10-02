namespace FolderDeck.Core.Tests;

public sealed class DuplicatePathLoadTests
{
    private static readonly Guid Id = Guid.Parse("77777777-8888-4999-8aaa-bbbbbbbbbbbb");

    [Fact]
    public void FoldersWithTheSamePathStillLoad()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 5,
              "id": "{{Id}}",
              "title": "경로가 겹친 파일",
              "folders": [
                { "id": "c1000000-0000-4000-8000-000000000001", "path": "D:\\같은곳" },
                { "id": "c1000000-0000-4000-8000-000000000002", "path": "D:\\같은곳" }
              ]
            }
            """);

        var result = temp.Store.LoadWorkspace(Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Folders.Count);
        Assert.All(result.Value.Folders, f => Assert.Equal(@"D:\같은곳", f.Path));
    }

    [Fact]
    public void DuplicateIdsAreStillRejected()
    {
        using var temp = new TempStore();
        temp.WriteRawWorkspace($"{Id}.json", $$"""
            {
              "schemaVersion": 5,
              "id": "{{Id}}",
              "title": "id 가 겹친 파일",
              "folders": [
                { "id": "c1000000-0000-4000-8000-000000000001", "path": "D:\\하나" },
                { "id": "c1000000-0000-4000-8000-000000000001", "path": "D:\\둘" }
              ]
            }
            """);

        Assert.False(temp.Store.LoadWorkspace(Id).IsSuccess);
    }
}
