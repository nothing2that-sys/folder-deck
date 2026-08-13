namespace FolderDeck.Core.Tests;

/// <summary>
/// 로드 검증은 <c>path</c> 중복을 보지 않는다 (§4.11-1 D3). <b>안 바뀐 것을 박는 테스트다.</b>
/// </summary>
/// <remarks>
/// 같은 경로를 두 번 등록하는 것은 UI 층에서만 거절한다. 파일을 여는 시점의 검증에 넣으면
/// json 을 손으로 고치다 경로가 겹친 사용자가 워크스페이스 자체를 못 열게 된다 — 그쪽이 더 나쁘다.
/// </remarks>
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

    /// <summary>중복 <b>id</b> 는 지금도 거절한다 — D3 이 푼 것은 path 쪽뿐이다.</summary>
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
