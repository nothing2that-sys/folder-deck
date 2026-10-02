using System.Collections.Concurrent;
using System.IO;
using FolderDeck.App.Interop;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

public enum EverythingSearchOutcome
{
    Ok,
    DllMissing,
    NotRunning,
    IndexNotLoaded,
    Failed,
}

public sealed record EverythingSearchResult(
    EverythingSearchOutcome Outcome,
    IReadOnlyList<FolderItem> Items,
    int TotalCount,
    bool Truncated);

public sealed class EverythingSearchService : IDisposable
{

    private const uint RequestFullPathAndFileName = 0x00000004;
    private const uint RequestSize = 0x00000010;
    private const uint RequestDateModified = 0x00000040;
    private const uint RequestAttributes = 0x00000100;
    private const uint RequestFlags =
        RequestFullPathAndFileName | RequestSize | RequestDateModified | RequestAttributes;

    private const uint SortNameAscending = 1;
    private const uint SortNameDescending = 2;
    private const uint SortSizeAscending = 5;
    private const uint SortSizeDescending = 6;
    private const uint SortExtensionAscending = 7;
    private const uint SortExtensionDescending = 8;
    private const uint SortDateModifiedAscending = 13;
    private const uint SortDateModifiedDescending = 14;

    private const uint FileAttributeDirectory = 0x00000010;

    private const uint EverythingErrorIpc = 2;

    private static readonly DateTime UnknownModifiedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private static readonly TimeSpan DisposeJoinTimeout = TimeSpan.FromSeconds(2);

    private readonly IEverythingNative _native;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public EverythingSearchService(IEverythingNative native)
    {
        _native = native;
        _thread = new Thread(RunQueue)
        {
            IsBackground = true,
            Name = "EverythingSearchThread",
        };
        _thread.Start();
    }

    public Task<EverythingSearchResult> SearchAsync(
        string query, SortBy sortBy, bool sortDesc, int maxResults, CancellationToken token)
    {
        var completion = new TaskCompletionSource<EverythingSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Add(() =>
        {
            try
            {

                if (token.IsCancellationRequested)
                {
                    completion.TrySetCanceled(token);
                    return;
                }

                var result = ExecuteSearch(query, sortBy, sortDesc, maxResults);
                if (token.IsCancellationRequested)
                {
                    completion.TrySetCanceled(token);
                }
                else
                {
                    completion.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private void RunQueue()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    private EverythingSearchResult ExecuteSearch(
        string query, SortBy sortBy, bool sortDesc, int maxResults)
    {
        using var guard = FpuGuard.Enter("EverythingSearch");

        try
        {

            var dbLoaded = _native.IsDbLoaded();
            var lastError = _native.GetLastError();

            if (lastError == EverythingErrorIpc)
            {
                return Empty(EverythingSearchOutcome.NotRunning);
            }

            if (!dbLoaded)
            {
                return Empty(EverythingSearchOutcome.IndexNotLoaded);
            }

            _native.Reset();
            _native.SetSearch(query);
            _native.SetRequestFlags(RequestFlags);
            _native.SetMax((uint)maxResults);
            _native.SetSort(MapSort(sortBy, sortDesc));

            if (!_native.Query(wait: true))
            {
                return Empty(EverythingSearchOutcome.Failed);
            }

            var numResults = _native.GetNumResults();
            var totalResults = _native.GetTotResults();
            var items = new List<FolderItem>((int)numResults);

            for (uint i = 0; i < numResults; i++)
            {
                var fullPath = _native.GetResultFullPath(i);
                if (fullPath is null)
                {
                    continue;
                }

                var attributes = _native.GetResultAttributes(i);
                var isDirectory = (attributes & FileAttributeDirectory) != 0;

                items.Add(new FolderItem(
                    Path.GetFileName(fullPath),
                    fullPath,
                    isDirectory,
                    isDirectory ? null : _native.GetResultSize(i),
                    _native.GetResultDateModifiedUtc(i) ?? UnknownModifiedUtc));
            }

            return new EverythingSearchResult(
                EverythingSearchOutcome.Ok, items, (int)totalResults, totalResults > numResults);
        }
        catch (DllNotFoundException)
        {
            return Empty(EverythingSearchOutcome.DllMissing);
        }
        catch (EntryPointNotFoundException)
        {
            return Empty(EverythingSearchOutcome.DllMissing);
        }
    }

    private static EverythingSearchResult Empty(EverythingSearchOutcome outcome) =>
        new(outcome, Array.Empty<FolderItem>(), 0, false);

    private static uint MapSort(SortBy sortBy, bool sortDesc) => sortBy switch
    {
        SortBy.Name => sortDesc ? SortNameDescending : SortNameAscending,
        SortBy.Size => sortDesc ? SortSizeDescending : SortSizeAscending,
        SortBy.Modified => sortDesc ? SortDateModifiedDescending : SortDateModifiedAscending,
        SortBy.Type => sortDesc ? SortExtensionDescending : SortExtensionAscending,
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, message: null),
    };

    public void Dispose()
    {
        _queue.CompleteAdding();

        if (_thread.Join(DisposeJoinTimeout))
        {
            _queue.Dispose();
        }
    }
}
