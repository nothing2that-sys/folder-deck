using System.ComponentModel;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;






public sealed class SearchTileTests
{


    private const uint EverythingErrorIpc = 2;


    [Fact]
    public void IpcErrorMeansNotRunning()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, EverythingErrorIpc, dbLoaded: true);

        Assert.Equal(EverythingAvailability.NotRunning, result);
    }


    [Fact]
    public void Version140IsTooOld()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 0, lastError: 0, dbLoaded: true);

        Assert.Equal(EverythingAvailability.VersionTooOld, result);
    }


    [Fact]
    public void Version141IsNotTooOld()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: true);

        Assert.NotEqual(EverythingAvailability.VersionTooOld, result);
    }


    [Fact]
    public void DbNotLoadedMeansIndexNotLoaded()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: false);

        Assert.Equal(EverythingAvailability.IndexNotLoaded, result);
    }


    [Fact]
    public void HealthyValuesMeanRunning()
    {
        var result = EverythingProbe.Decide(major: 1, minor: 4, revision: 1, lastError: 0, dbLoaded: true);

        Assert.Equal(EverythingAvailability.Running, result);
    }


    [Fact]
    public void IpcErrorWinsBeforeTheVersionCheck()
    {


        var result = EverythingProbe.Decide(major: 0, minor: 0, revision: 0, EverythingErrorIpc, dbLoaded: false);

        Assert.Equal(EverythingAvailability.NotRunning, result);
    }



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


    [Fact]
    public void SearchTilePanelIsNotEmptyEvenWithNoEntry()
    {
        var panel = MakePanel(isSearchTile: true);

        Assert.Null(panel.Entry);
        Assert.False(panel.IsEmpty);
    }


    [Fact]
    public void FolderTilePanelIsEmptyWhenEntryIsNull()
    {
        var panel = MakePanel(isSearchTile: false);

        Assert.Null(panel.Entry);
        Assert.True(panel.IsEmpty);
    }


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





public sealed class SearchTileDragAndSortTests
{
    private static FileItemViewModel MakeItem(string path, bool isDirectory = false) =>
        new(new FolderItem(Path.GetFileName(path), path, isDirectory, isDirectory ? null : 0, DateTime.UtcNow));

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




    [Fact]
    public void SearchTileDragPayloadIsNotNullEvenWithoutCurrentPath()
    {
        var panel = MakePanel(isSearchTile: true);
        Assert.Null(panel.CurrentPath);
        panel.SelectedItems = [MakeItem(@"C:\a\one.txt")];

        var payload = panel.BuildDragPayload();

        Assert.NotNull(payload);
    }


    [Fact]
    public void FolderTileDragPayloadIsStillNullWhenCurrentPathIsNull()
    {
        var panel = MakePanel(isSearchTile: false);
        Assert.Null(panel.CurrentPath);
        panel.SelectedItems = [MakeItem(@"C:\a\one.txt")];

        var payload = panel.BuildDragPayload();

        Assert.Null(payload);
    }


    [Fact]
    public void SearchTilePayloadItemsMatchTheDraggedItem()
    {
        var panel = MakePanel(isSearchTile: true);
        var inSelection = MakeItem(@"C:\a\one.txt");
        var outsideSelection = MakeItem(@"C:\a\two.txt");
        panel.SelectedItems = [inSelection];


        var payload = panel.BuildDragPayload(pressedItem: outsideSelection);

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(outsideSelection.FullPath, item.Path);
    }


    [Fact]
    public void SearchTilePayloadSourceFolderIsTheFirstItemsParent()
    {
        var panel = MakePanel(isSearchTile: true);
        var first = MakeItem(@"C:\a\sub\one.txt");
        var second = MakeItem(@"C:\other\two.txt");
        panel.SelectedItems = [first, second];

        var payload = panel.BuildDragPayload();

        Assert.NotNull(payload);
        Assert.Equal(@"C:\a\sub", payload!.SourceFolder);
    }




    [Fact]
    public async Task SettingSortToSizeRequeriesInSizeAscendingOrder()
    {
        const uint sortSizeAscending = 5;
        var native = new RecordingEverythingNative();
        var panel = MakeSearchPanel(native);
        panel.SearchText = "x";
        await WaitUntilAsync(panel, () => native.LastSetSort is not null, TimeSpan.FromSeconds(3));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        var arrived = await WaitUntilAsync(
            panel, () => native.LastSetSort == sortSizeAscending, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "재질의가 일어나지 않았다.");
    }


    [Fact]
    public async Task ClickingTheSameCriterionAgainRequeriesDescending()
    {
        const uint sortSizeAscending = 5;
        const uint sortSizeDescending = 6;
        var native = new RecordingEverythingNative();
        var panel = MakeSearchPanel(native);
        panel.SearchText = "x";
        await WaitUntilAsync(panel, () => native.LastSetSort is not null, TimeSpan.FromSeconds(3));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        await WaitUntilAsync(panel, () => native.LastSetSort == sortSizeAscending, TimeSpan.FromSeconds(3));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        var arrived = await WaitUntilAsync(
            panel, () => native.LastSetSort == sortSizeDescending, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "내림차순 재질의가 일어나지 않았다.");
    }


    [Fact]
    public async Task SwitchingToADifferentCriterionGoesBackToAscending()
    {
        const uint sortSizeAscending = 5;
        const uint sortSizeDescending = 6;
        const uint sortDateModifiedAscending = 13;
        var native = new RecordingEverythingNative();
        var panel = MakeSearchPanel(native);
        panel.SearchText = "x";
        await WaitUntilAsync(panel, () => native.LastSetSort is not null, TimeSpan.FromSeconds(3));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        await WaitUntilAsync(panel, () => native.LastSetSort == sortSizeAscending, TimeSpan.FromSeconds(3));
        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        await WaitUntilAsync(panel, () => native.LastSetSort == sortSizeDescending, TimeSpan.FromSeconds(3));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);
        var arrived = await WaitUntilAsync(
            panel, () => native.LastSetSort == sortDateModifiedAscending, TimeSpan.FromSeconds(3));

        Assert.True(arrived, "다른 기준으로 바뀌며 오름차순으로 재질의되지 않았다.");
    }


    [Fact]
    public async Task SearchTileSortNeverTouchesEntryEvenIfOneIsForced()
    {
        var panel = MakePanel(isSearchTile: true);
        var entry = new FolderEntry { Path = @"C:\forced", DisplayName = "억지로 넣은 Entry" };
        panel.Entry = entry;
        var originalSortBy = entry.SortBy;
        var originalSortDesc = entry.SortDesc;

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(originalSortBy, entry.SortBy);
        Assert.Equal(originalSortDesc, entry.SortDesc);
    }


    [Fact]
    public void SearchTileDefaultSortIsNameAscending()
    {
        var panel = MakePanel(isSearchTile: true);

        Assert.Equal("이름 ↑", panel.NameHeaderText);
        Assert.Equal("크기", panel.SizeHeaderText);
        Assert.Equal("수정", panel.ModifiedHeaderText);
    }


    [Fact]
    public async Task FolderTileSortStillMutatesEntryAndSaves()
    {
        var entry = new FolderEntry { Path = @"C:\a", DisplayName = "a", SortBy = SortBy.Name, SortDesc = false };
        var panel = MakePanel(isSearchTile: false);
        panel.Entry = entry;
        var saveCalls = 0;
        panel.SaveFolderEntry = () => saveCalls++;

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, entry.SortBy);
        Assert.False(entry.SortDesc);
        Assert.Equal(1, saveCalls);
    }
}







internal sealed class RecordingEverythingNative : IEverythingNative
{
    public string? LastSearch { get; private set; }

    public uint? LastMax { get; private set; }

    public uint? LastSetSort { get; private set; }

    public bool IsDbLoadedResult { get; set; } = true;

    public uint LastErrorResult { get; set; }

    public bool QueryResult { get; set; } = true;

    public uint? TotResultsOverride { get; set; }

    public List<FakeItem> Results { get; } = [];

    public void SetSearch(string query) => LastSearch = query;

    public void SetMax(uint max) => LastMax = max;

    public void SetSort(uint sort) => LastSetSort = sort;

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
