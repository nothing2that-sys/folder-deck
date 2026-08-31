using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="EverythingSearchService"/> — 전역 검색 타일(§4.12)의 질의 계층(Phase 12 · D69-b).
/// <see cref="IEverythingNative"/> 가짜 하나로 문다 — 실제 <c>Everything64.dll</c> 을 부르지
/// 않는다(§6-3). 지시서 표의 14개를 정확히 그대로 옮긴다 — 더 만들지도 합치지도 않는다.
/// </summary>
public sealed class EverythingSearchServiceTests
{
    private static FakeEverythingNative HealthyFake() => new()
    {
        IsDbLoadedResult = true,
        LastErrorResult = 0,
        QueryResult = true,
    };

    // 1
    [Fact]
    public async Task NameSortMapsToNameAscendingAndDescending()
    {
        var ascending = HealthyFake();
        using var service1 = new EverythingSearchService(ascending);
        await service1.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);
        Assert.Equal(1u, ascending.LastSetSort);

        var descending = HealthyFake();
        using var service2 = new EverythingSearchService(descending);
        await service2.SearchAsync("q", SortBy.Name, sortDesc: true, 100, CancellationToken.None);
        Assert.Equal(2u, descending.LastSetSort);
    }

    // 2
    [Fact]
    public async Task SizeSortMapsToSizeAscendingAndDescending()
    {
        var ascending = HealthyFake();
        using var service1 = new EverythingSearchService(ascending);
        await service1.SearchAsync("q", SortBy.Size, sortDesc: false, 100, CancellationToken.None);
        Assert.Equal(5u, ascending.LastSetSort);

        var descending = HealthyFake();
        using var service2 = new EverythingSearchService(descending);
        await service2.SearchAsync("q", SortBy.Size, sortDesc: true, 100, CancellationToken.None);
        Assert.Equal(6u, descending.LastSetSort);
    }

    // 3
    [Fact]
    public async Task ModifiedSortMapsToDateModifiedAscendingAndDescending()
    {
        var ascending = HealthyFake();
        using var service1 = new EverythingSearchService(ascending);
        await service1.SearchAsync("q", SortBy.Modified, sortDesc: false, 100, CancellationToken.None);
        Assert.Equal(13u, ascending.LastSetSort);

        var descending = HealthyFake();
        using var service2 = new EverythingSearchService(descending);
        await service2.SearchAsync("q", SortBy.Modified, sortDesc: true, 100, CancellationToken.None);
        Assert.Equal(14u, descending.LastSetSort);
    }

    // 4
    [Fact]
    public async Task TypeSortMapsToExtensionAscendingAndDescending()
    {
        var ascending = HealthyFake();
        using var service1 = new EverythingSearchService(ascending);
        await service1.SearchAsync("q", SortBy.Type, sortDesc: false, 100, CancellationToken.None);
        Assert.Equal(7u, ascending.LastSetSort);

        var descending = HealthyFake();
        using var service2 = new EverythingSearchService(descending);
        await service2.SearchAsync("q", SortBy.Type, sortDesc: true, 100, CancellationToken.None);
        Assert.Equal(8u, descending.LastSetSort);
    }

    // 5
    [Fact]
    public async Task RequestFlagsTurnsOnAllFour()
    {
        var fake = HealthyFake();
        using var service = new EverythingSearchService(fake);

        await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        const uint fullPathAndFileName = 0x00000004;
        const uint size = 0x00000010;
        const uint dateModified = 0x00000040;
        const uint attributes = 0x00000100;
        Assert.Equal(fullPathAndFileName | size | dateModified | attributes, fake.LastSetRequestFlags);
    }

    // 6
    [Fact]
    public async Task MaxResultsPassesThroughToSetMax()
    {
        var fake = HealthyFake();
        using var service = new EverythingSearchService(fake);

        await service.SearchAsync("q", SortBy.Name, sortDesc: false, 2000, CancellationToken.None);

        Assert.Equal(2000u, fake.LastSetMax);
    }

    // 7
    [Fact]
    public async Task TruncatedWhenTotalExceedsReturned()
    {
        var fake = HealthyFake();
        fake.Results.Add(new FakeResult(@"C:\a\one.txt", 0, 1, DateTime.UtcNow));
        fake.Results.Add(new FakeResult(@"C:\a\two.txt", 0, 2, DateTime.UtcNow));
        fake.TotResultsOverride = 5;
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 2, CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(5, result.TotalCount);
    }

    // 8
    [Fact]
    public async Task NotTruncatedWhenTotalEqualsReturned()
    {
        var fake = HealthyFake();
        fake.Results.Add(new FakeResult(@"C:\a\one.txt", 0, 1, DateTime.UtcNow));
        fake.Results.Add(new FakeResult(@"C:\a\two.txt", 0, 2, DateTime.UtcNow));
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalCount);
    }

    // 9
    [Fact]
    public async Task FileItemHasLeafNameSizeAndUtcModified()
    {
        var fake = HealthyFake();
        var modified = DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc);
        fake.Results.Add(new FakeResult(@"C:\folder\readme.txt", 0, 1234, modified));
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("readme.txt", item.Name);
        Assert.Equal(1234, item.Size);
        Assert.Equal(DateTimeKind.Utc, item.ModifiedUtc.Kind);
    }

    // 10
    [Fact]
    public async Task DirectoryAttributeMakesIsDirectoryTrueAndSizeNull()
    {
        const uint fileAttributeDirectory = 0x00000010;
        var fake = HealthyFake();
        // Size 는 일부러 값을 채운다 — 폴더면 서비스가 이 값을 아예 안 쓰는지를 확인한다.
        fake.Results.Add(new FakeResult(@"C:\folder\sub", fileAttributeDirectory, 999, DateTime.UtcNow));
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.True(item.IsDirectory);
        Assert.Null(item.Size);
    }

    // 11
    [Fact]
    public async Task NullFullPathItemIsSkipped()
    {
        var fake = HealthyFake();
        fake.Results.Add(new FakeResult(null, 0, 1, DateTime.UtcNow));
        fake.Results.Add(new FakeResult(@"C:\a\kept.txt", 0, 2, DateTime.UtcNow));
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("kept.txt", item.Name);
    }

    // 12
    [Fact]
    public async Task IndexNotLoadedSkipsQuery()
    {
        var fake = HealthyFake();
        fake.IsDbLoadedResult = false;
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        Assert.Equal(EverythingSearchOutcome.IndexNotLoaded, result.Outcome);
        Assert.False(fake.QueryCalled);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.Truncated);
    }

    // 13
    [Fact]
    public async Task NotRunningSkipsQuery()
    {
        const uint everythingErrorIpc = 2;
        var fake = HealthyFake();
        fake.LastErrorResult = everythingErrorIpc;
        using var service = new EverythingSearchService(fake);

        var result = await service.SearchAsync("q", SortBy.Name, sortDesc: false, 100, CancellationToken.None);

        Assert.Equal(EverythingSearchOutcome.NotRunning, result.Outcome);
        Assert.False(fake.QueryCalled);
    }

    // 14
    [Fact]
    public async Task ConcurrentSearchesDoNotOverlapAndRunOnOneNonCallerThread()
    {
        var fake = HealthyFake();
        fake.Results.Add(new FakeResult(@"C:\a\one.txt", 0, 1, DateTime.UtcNow));
        fake.OnQuery = () => Thread.Sleep(50);
        using var service = new EverythingSearchService(fake);

        var callerThreadId = Environment.CurrentManagedThreadId;

        var first = service.SearchAsync("a", SortBy.Name, sortDesc: false, 100, CancellationToken.None);
        var second = service.SearchAsync("b", SortBy.Name, sortDesc: false, 100, CancellationToken.None);
        await Task.WhenAll(first, second);

        var distinctThreads = fake.CallThreadIds.Distinct().ToList();
        var threadId = Assert.Single(distinctThreads);
        Assert.NotEqual(callerThreadId, threadId);
    }

    private sealed record FakeResult(string? FullPath, uint Attributes, long? Size, DateTime? ModifiedUtc);

    private sealed class FakeEverythingNative : IEverythingNative
    {
        private readonly object _gate = new();

        public List<string> Calls { get; } = new();
        public List<int> CallThreadIds { get; } = new();
        public List<FakeResult> Results { get; } = new();
        public uint? LastSetMax { get; private set; }
        public uint? LastSetSort { get; private set; }
        public uint? LastSetRequestFlags { get; private set; }
        public bool QueryCalled { get; private set; }
        public bool IsDbLoadedResult { get; set; } = true;
        public uint LastErrorResult { get; set; }
        public bool QueryResult { get; set; } = true;
        public uint? TotResultsOverride { get; set; }
        public Action? OnQuery { get; set; }

        private void Record(string name)
        {
            lock (_gate)
            {
                Calls.Add(name);
                CallThreadIds.Add(Environment.CurrentManagedThreadId);
            }
        }

        public void SetSearch(string query) => Record(nameof(SetSearch));

        public void SetMax(uint max)
        {
            Record(nameof(SetMax));
            LastSetMax = max;
        }

        public void SetSort(uint sort)
        {
            Record(nameof(SetSort));
            LastSetSort = sort;
        }

        public void SetRequestFlags(uint flags)
        {
            Record(nameof(SetRequestFlags));
            LastSetRequestFlags = flags;
        }

        public bool Query(bool wait)
        {
            Record(nameof(Query));
            QueryCalled = true;
            OnQuery?.Invoke();
            return QueryResult;
        }

        public uint GetNumResults()
        {
            Record(nameof(GetNumResults));
            return (uint)Results.Count;
        }

        public uint GetTotResults()
        {
            Record(nameof(GetTotResults));
            return TotResultsOverride ?? (uint)Results.Count;
        }

        public uint GetLastError()
        {
            Record(nameof(GetLastError));
            return LastErrorResult;
        }

        public bool IsDbLoaded()
        {
            Record(nameof(IsDbLoaded));
            return IsDbLoadedResult;
        }

        public string? GetResultFullPath(uint index)
        {
            Record(nameof(GetResultFullPath));
            return Results[(int)index].FullPath;
        }

        public long? GetResultSize(uint index)
        {
            Record(nameof(GetResultSize));
            return Results[(int)index].Size;
        }

        public DateTime? GetResultDateModifiedUtc(uint index)
        {
            Record(nameof(GetResultDateModifiedUtc));
            return Results[(int)index].ModifiedUtc;
        }

        public uint GetResultAttributes(uint index)
        {
            Record(nameof(GetResultAttributes));
            return Results[(int)index].Attributes;
        }

        public void Reset() => Record(nameof(Reset));
    }
}
