using System.ComponentModel;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 전역 검색 타일(§4.12, Phase 12 · D69-e)의 판정 순수 함수와 <see cref="FolderPanelViewModel"/>
/// 검색 타일 모드 · <see cref="TileViewModel"/> 배선. 지시서 표의 14개를 정확히 그대로 옮긴다 —
/// 더 만들지도 합치지도 않는다.
/// </summary>
public sealed class SearchTileTests
{
    // ── Decide() — 판정 순수 함수(1~6) ──────────────────────────────────────

    private const uint EverythingErrorIpc = 2;

    // 1
    [Fact]
    public void IpcErrorMeansNotRunning()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, EverythingErrorIpc, dbLoaded: true);

        Assert.Equal(EverythingAvailability.NotRunning, result);
    }

    // 2
    [Fact]
    public void Version140IsTooOld()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 0, lastError: 0, dbLoaded: true);

        Assert.Equal(EverythingAvailability.VersionTooOld, result);
    }

    // 3
    [Fact]
    public void Version141IsNotTooOld()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: true);

        Assert.NotEqual(EverythingAvailability.VersionTooOld, result);
    }

    // 4
    [Fact]
    public void DbNotLoadedMeansIndexNotLoaded()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: false);

        Assert.Equal(EverythingAvailability.IndexNotLoaded, result);
    }

    // 5
    [Fact]
    public void HealthyValuesMeanRunning()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: true);

        Assert.Equal(EverythingAvailability.Running, result);
    }

    // 6
    [Fact]
    public void IpcErrorWinsBeforeTheVersionCheck()
    {
        // 버전 숫자만 보면 VersionTooOld 로도 읽힐 값(0.0.0)이지만, IPC 오류가 나 있으면
        // 그 값 자체가 무의미하다 — NotRunning 이 이긴다.
        var result = EverythingProbe.Decide(major: 0, minor: 0, revision: 0, EverythingErrorIpc, dbLoaded: false);

        Assert.Equal(EverythingAvailability.NotRunning, result);
    }

    // ── FolderPanelViewModel 검색 타일 모드(7~13) ────────────────────────────

    private static FolderPanelViewModel MakePanel(bool isSearchTile) => new(
        new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
        isRotating: false, isSearchTile);

    private static FolderPanelViewModel MakeSearchPanel(RecordingEverythingNative native, int maxResults = 500)
    {
        var panel = MakePanel(isSearchTile: true);
        panel.EverythingSearchService = new EverythingSearchService(native);
        panel.EverythingMaxResults = maxResults;
        return panel;
    }

    /// <summary>
    /// <paramref name="predicate"/> 가 참이 될 때까지 <see cref="FolderPanelViewModel.PropertyChanged"/>
    /// 를 기다린다. 검색은 디바운스(250ms) 뒤 전용 스레드에서 도는 비동기 발사-후-망각이라
    /// (<see cref="ShellIconBindingTests"/> 의 <c>WaitForShellIconAsync</c> 와 같은 이유로)
    /// 직접 <c>await</c> 할 자리가 없다.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(
        FolderPanelViewModel panel, Func<bool> predicate, TimeSpan timeout)
    {
        if (predicate())
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (predicate())
            {
                tcs.TrySetResult(true);
            }
        }

        panel.PropertyChanged += Handler;
        try
        {
            if (predicate())
            {
                return true;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(true);
            return completed == tcs.Task;
        }
        finally
        {
            panel.PropertyChanged -= Handler;
        }
    }

    // 7
    [Fact]
    public void SearchTilePanelIsNotEmptyEvenWithNoEntry()
    {
        var panel = MakePanel(isSearchTile: true);

        Assert.Null(panel.Entry);
        Assert.False(panel.IsEmpty);
    }

    // 8
    [Fact]
    public void FolderTilePanelIsEmptyWhenEntryIsNull()
    {
        var panel = MakePanel(isSearchTile: false);

        Assert.Null(panel.Entry);
        Assert.True(panel.IsEmpty);
    }

    // 9
    [Fact]
    public async Task TypingAQueryCallsTheServiceWithThatString()
    {
        var native = new RecordingEverythingNative();
        var panel = MakeSearchPanel(native);

        panel.SearchText = "readme";

        var arrived = await WaitUntilAsync(panel, () => native.LastSearch is not null, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "서비스가 불리지 않았다.");
        Assert.Equal("readme", native.LastSearch);
    }

    // 10
    [Fact]
    public async Task TypingAQueryCallsTheServiceWithTheConfiguredMax()
    {
        var native = new RecordingEverythingNative();
        var panel = MakeSearchPanel(native, maxResults: 2000);

        panel.SearchText = "readme";

        var arrived = await WaitUntilAsync(panel, () => native.LastMax is not null, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "서비스가 불리지 않았다.");
        Assert.Equal(2000u, native.LastMax);
    }

    // 11
    [Fact]
    public async Task OkResultFillsDisplayItems()
    {
        var native = new RecordingEverythingNative();
        native.Results.Add(new RecordingEverythingNative.FakeItem(@"C:\a\one.txt", 0, 10, DateTime.UtcNow));
        var panel = MakeSearchPanel(native);

        panel.SearchText = "one";

        var arrived = await WaitUntilAsync(panel, () => panel.DisplayItems.Count > 0, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "결과가 도착하지 않았다.");
        var item = Assert.Single(panel.DisplayItems);
        Assert.Equal("one.txt", item.Name);
    }

    // 12
    [Fact]
    public async Task NotOkResultEmptiesDisplayItemsAndSetsAStatusMessage()
    {
        var native = new RecordingEverythingNative { LastErrorResult = EverythingErrorIpc };
        var panel = MakeSearchPanel(native);

        panel.SearchText = "one";

        var arrived = await WaitUntilAsync(
            panel, () => panel.EverythingStatusText is not null, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "상태 문구가 서지 않았다.");
        Assert.Empty(panel.DisplayItems);
        Assert.Equal(
            new EverythingStatus(EverythingAvailability.NotRunning, null).Describe(),
            panel.EverythingStatusText);
    }

    // 13
    [Fact]
    public async Task TruncatedResultsShowBothTheTotalAndTheShownCount()
    {
        var native = new RecordingEverythingNative { TotResultsOverride = 5 };
        native.Results.Add(new RecordingEverythingNative.FakeItem(@"C:\a\one.txt", 0, 1, DateTime.UtcNow));
        native.Results.Add(new RecordingEverythingNative.FakeItem(@"C:\a\two.txt", 0, 2, DateTime.UtcNow));
        var panel = MakeSearchPanel(native);

        panel.SearchText = "t";

        var arrived = await WaitUntilAsync(panel, () => panel.DisplayItems.Count > 0, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "결과가 도착하지 않았다.");
        Assert.Equal("5개 중 2개", panel.ItemCountText);
    }

    // ── TileViewModel(14) ────────────────────────────────────────────────────

    // 14
    [Fact]
    public void SearchTileViewModelReportsIsSearchAndKoreanKindTag()
    {
        var spec = new TileSpec { Kind = TileKind.Search, CellX = 0, CellY = 0 };
        var panel = MakePanel(isSearchTile: true);

        var tile = new TileViewModel(spec, panel);

        Assert.True(tile.IsSearch);
        Assert.Equal("검색", tile.KindTag);
    }
}

/// <summary>
/// 검색어·상한을 실제로 기록하는 <see cref="IEverythingNative"/> 가짜 — 전역 검색 타일이
/// <see cref="EverythingSearchService"/> 에 무엇을 넘기는지까지 확인해야 해서(§6-5 시험
/// 9·10), <c>EverythingSearchServiceTests</c> 의 <c>FakeEverythingNative</c>(호출 순서만
/// 기록)와는 따로 둔다 — 그 클래스는 <c>private</c> 이라 갈아 쓸 수도 없다(D69-e 6-5).
/// </summary>
internal sealed class RecordingEverythingNative : IEverythingNative
{
    public string? LastSearch { get; private set; }

    public uint? LastMax { get; private set; }

    public bool IsDbLoadedResult { get; set; } = true;

    public uint LastErrorResult { get; set; }

    public bool QueryResult { get; set; } = true;

    public uint? TotResultsOverride { get; set; }

    public List<FakeItem> Results { get; } = [];

    public void SetSearch(string query) => LastSearch = query;

    public void SetMax(uint max) => LastMax = max;

    public void SetSort(uint sort)
    {
    }

    public void SetRequestFlags(uint flags)
    {
    }

    public bool Query(bool wait) => QueryResult;

    public uint GetNumResults() => (uint)Results.Count;

    public uint GetTotResults() => TotResultsOverride ?? (uint)Results.Count;

    public uint GetLastError() => LastErrorResult;

    public bool IsDbLoaded() => IsDbLoadedResult;

    public string? GetResultFullPath(uint index) => Results[(int)index].FullPath;

    public long? GetResultSize(uint index) => Results[(int)index].Size;

    public DateTime? GetResultDateModifiedUtc(uint index) => Results[(int)index].ModifiedUtc;

    public uint GetResultAttributes(uint index) => Results[(int)index].Attributes;

    public void Reset()
    {
    }

    public sealed record FakeItem(string? FullPath, uint Attributes, long? Size, DateTime? ModifiedUtc);
}
