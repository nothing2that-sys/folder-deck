using System.Collections.Concurrent;
using System.IO;
using FolderDeck.App.Interop;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>전역 검색 타일(§4.12)의 native 호출 결과 판정 — 다섯 갈래(§4.12-6). <c>Failed</c> 는
/// 질의 자체의 실패고 나머지 넷과는 종류가 다르다.</summary>
public enum EverythingSearchOutcome
{
    Ok,
    DllMissing,
    NotRunning,
    IndexNotLoaded,
    Failed,
}

/// <summary>
/// 검색 한 번의 결과. <see cref="Outcome"/> 이 <see cref="EverythingSearchOutcome.Ok"/> 가
/// 아니면 <see cref="Items"/> 는 빈 목록·<see cref="TotalCount"/> 는 0·<see cref="Truncated"/>
/// 는 false 다(§6-2).
/// </summary>
public sealed record EverythingSearchResult(
    EverythingSearchOutcome Outcome,
    IReadOnlyList<FolderItem> Items,
    int TotalCount,
    bool Truncated);

/// <summary>
/// Everything(voidtools) 질의 계층 — 전역 검색 타일(§4.12)이 부르는 서비스다(Phase 12 · D69-b).
/// <b>이 회차는 화면을 붙이지 않는다</b> — 부르는 자리는 D69-c 의 몫이다.
/// </summary>
/// <remarks>
/// <b>직렬화</b>: Everything 은 검색 조건도 결과도 프로세스에 하나뿐이다(§4.12-3) — 그래서
/// <see cref="IEverythingNative"/> 호출은 전부 이 서비스가 띄우는 <b>전용 스레드 하나</b>에서만
/// 일어난다. <see cref="System.Threading.Tasks.Task.Run(Action)"/> 으로 스레드풀에 던지면
/// 스레드풀 스레드끼리 호출이 겹칠 수 있어 쓰지 않는다.
/// </remarks>
public sealed class EverythingSearchService : IDisposable
{
    // Everything.h 실물에서 읽은 값이다(D69-b §5). 요청 플래그 넷 — 전체 경로 · 크기 ·
    // 수정일 · 속성(§4.12-3).
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

    /// <summary><c>Everything.h:43</c> — "Everything search client is not running".</summary>
    private const uint EverythingErrorIpc = 2;

    /// <summary>
    /// 수정일을 못 받은 항목의 <see cref="FolderItem.ModifiedUtc"/> 폴백(§6-4 · D69-c).
    /// <c>DateTime.MinValue</c> 는 <c>Kind == Unspecified</c> 라 계약을 어긴다 — <c>FolderItem</c>
    /// 은 UTC 를 약속한다.
    /// </summary>
    private static readonly DateTime UnknownModifiedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// <see cref="Dispose"/> 가 <c>_thread.Join()</c> 에 거는 시한(§6-4 · D69-c). 질의가 도는
    /// 중에 창을 닫아도 UI 가 오래 막히지 않게 하는 값이지, native 호출의 실측 소요를 재서
    /// 정한 값은 아니다 — 스레드는 <c>IsBackground</c> 라 시한을 넘겨도 프로세스 종료를
    /// 막지 않는다(보고서 §4).
    /// </summary>
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

    /// <summary>
    /// 검색 하나를 전용 스레드에 넣고 끝나기를 기다린다. 호출자 스레드는 막지 않는다.
    /// <paramref name="token"/> 이 취소돼도 <b>도는 질의를 끊지 않는다</b>(§4.12-3) — 이미
    /// 시작한 질의는 끝까지 두고 결과만 버린다(취소된 태스크로 끝난다).
    /// </summary>
    public Task<EverythingSearchResult> SearchAsync(
        string query, SortBy sortBy, bool sortDesc, int maxResults, CancellationToken token)
    {
        var completion = new TaskCompletionSource<EverythingSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Add(() =>
        {
            try
            {
                // 시작 전에 이미 취소됐으면 native 를 한 번도 부르지 않는다(§6-4 · D69-c).
                // 도는 질의를 끊는 것이 아니다(§4.12-3) — 아직 시작하지 않은 것만 건너뛴다.
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
            // 건강 검사가 질의보다 먼저다(§6-2). GetLastError() 를 IsDbLoaded() 먼저로
            // 본다 — EverythingProbe(D65)와 같은 결이다: Everything 이 안 돌고 있으면
            // IsDbLoaded() 도 false 를 주므로, IPC 오류를 먼저 가르지 않으면 "안 돎"이
            // 전부 "색인 미적재"로 뭉개진다.
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

        // 시한 안에 스레드가 안 끝나면 그냥 돌아온다 — 스레드는 IsBackground 라 프로세스
        // 종료를 막지 않는다(§6-4 · D69-c). _queue.Dispose() 는 스레드가 실제로 끝났을 때만
        // 부른다 — 아직 도는 스레드가 나중에 disposed 된 컬렉션을 건드리면(GetConsumingEnumerable
        // 의 다음 MoveNext) 배경 스레드에서 처리되지 않은 예외가 나 프로세스를 죽일 수 있다.
        if (_thread.Join(DisposeJoinTimeout))
        {
            _queue.Dispose();
        }
    }
}
