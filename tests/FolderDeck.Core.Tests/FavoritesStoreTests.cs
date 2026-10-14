using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>공유 즐겨찾기 저장소 (2026-10-14 S3).</summary>
public sealed class FavoritesStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FolderDeckPaths _paths;

    public FavoritesStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.FavoritesTests", Guid.NewGuid().ToString("N"));
        _paths = new FolderDeckPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 무시.
        }
    }

    [Fact]
    public void NoFileReadsAsAnEmptyList()
    {
        var loaded = new FavoritesStore(_paths).Load();

        Assert.Null(loaded.Failure);
        Assert.Empty(loaded.Value!);
    }

    [Fact]
    public void AnAddedFavoriteRoundTrips()
    {
        var store = new FavoritesStore(_paths);

        var added = store.Add(@"C:\Work\Logs\", "로그");

        Assert.True(added.Value!.Changed);
        var item = Assert.Single(store.Load().Value!);
        Assert.Equal(@"C:\Work\Logs", item.Path);
        Assert.Equal("로그", item.DisplayName);
    }

    [Fact]
    public void TheSamePathIsNotAddedTwice()
    {
        var store = new FavoritesStore(_paths);
        store.Add(@"C:\Work\Logs");

        var again = store.Add(@"c:\work\logs\");

        Assert.False(again.Value!.Changed);
        Assert.Single(store.Load().Value!);
    }

    /// <summary>
    /// 이 회차의 핵심 불변 — 두 창(여기서는 두 저장소 인스턴스)이 번갈아 바꿔도 <b>둘의 변경이 다 남는다</b>.
    /// B 는 A 의 추가를 모르는 낡은 화면 목록을 들고 있지만, 쓰기는 디스크를 다시 읽고 하므로 A 의
    /// 추가를 지우지 않는다.
    /// </summary>
    [Fact]
    public void InterleavedChangesFromTwoWindowsAreBothKept()
    {
        var a = new FavoritesStore(_paths);
        var b = new FavoritesStore(_paths);
        var staleListInB = b.Load().Value!;

        a.Add(@"C:\From\A");
        b.Add(@"C:\From\B");
        a.Add(@"C:\From\A2");

        Assert.Empty(staleListInB);
        Assert.Equal(
            [@"C:\From\A", @"C:\From\B", @"C:\From\A2"],
            new FavoritesStore(_paths).Load().Value!.Select(i => i.Path));
    }

    [Fact]
    public void RemovingInOneWindowKeepsAnAddFromTheOther()
    {
        var a = new FavoritesStore(_paths);
        var b = new FavoritesStore(_paths);
        a.Add(@"C:\One");
        var one = a.Load().Value!.Single();

        b.Add(@"C:\Two");
        a.Remove(one.Id);

        Assert.Equal([@"C:\Two"], b.Load().Value!.Select(i => i.Path));
    }

    [Fact]
    public void MovingShiftsOneSlotAndStopsAtTheEnds()
    {
        var store = new FavoritesStore(_paths);
        store.Add(@"C:\A");
        store.Add(@"C:\B");
        store.Add(@"C:\C");
        var c = store.Load().Value![2];

        Assert.True(store.Move(c.Id, -1).Value!.Changed);
        Assert.Equal([@"C:\A", @"C:\C", @"C:\B"], store.Load().Value!.Select(i => i.Path));

        var a = store.Load().Value![0];
        Assert.False(store.Move(a.Id, -1).Value!.Changed);
    }

    /// <summary>손상된 파일 위에는 쓰지 않는다 — 바이트 그대로인지 비교한다.</summary>
    [Fact]
    public void AMutationOnACorruptFileFailsAndLeavesTheBytesAlone()
    {
        var corrupt = "{ \"schemaVersion\": 1, \"items\": [ { \"path\": "u8.ToArray();
        File.WriteAllBytes(_paths.FavoritesFile, corrupt);

        var result = new FavoritesStore(_paths).Add(@"C:\New");

        Assert.Equal(StorageFailureKind.CorruptData, result.Failure!.Kind);
        Assert.Equal(corrupt, File.ReadAllBytes(_paths.FavoritesFile));
    }

    [Fact]
    public void ANewerSchemaIsRejectedAndNotOverwritten()
    {
        var json = $"{{ \"schemaVersion\": {FavoritesDocument.CurrentSchemaVersion + 1}, \"items\": [] }}";
        File.WriteAllText(_paths.FavoritesFile, json);

        var result = new FavoritesStore(_paths).Add(@"C:\New");

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, result.Failure!.Kind);
        Assert.Equal(json, File.ReadAllText(_paths.FavoritesFile));
    }

    [Fact]
    public void BlankEntriesInAHandEditedFileAreSkippedNotFatal()
    {
        File.WriteAllText(_paths.FavoritesFile,
            "{ \"schemaVersion\": 1, \"items\": [ null, { \"path\": \"\" }, { \"path\": \"C:\\\\Ok\" } ] }");

        var loaded = new FavoritesStore(_paths).Load();

        Assert.Null(loaded.Failure);
        Assert.Equal([@"C:\Ok"], loaded.Value!.Select(i => i.Path));
    }
}
