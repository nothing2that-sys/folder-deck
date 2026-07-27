namespace FolderDeck.Core.Storage;

/// <summary>
/// "변경 즉시 저장(디바운스)" (§5) 의 디바운스 부분. 같은 키에 대한 연속 요청은
/// 마지막 것만 살아남고, 조용해진 뒤 <see cref="Delay"/> 만큼 지나면 실제로 쓴다.
/// </summary>
/// <remarks>
/// 쓰기는 스레드 풀에서 일어나므로 <see cref="Failed"/> 도 UI 스레드가 아닌 곳에서 올라온다.
/// 백그라운드 쓰기 실패가 조용히 사라지면 §5 의 "실패를 드러낸다"가 깨지므로,
/// App 은 이 이벤트를 반드시 구독해 사용자에게 알려야 한다.
/// 앱 종료 직전에는 <see cref="Flush"/> 로 밀린 쓰기를 마무리한다.
/// </remarks>
public sealed class DebouncedSaveScheduler : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Pending> _pending = [];
    private bool _disposed;

    public DebouncedSaveScheduler(TimeSpan? delay = null)
    {
        Delay = delay ?? TimeSpan.FromMilliseconds(400);
    }

    /// <summary>백그라운드 쓰기 실패. UI 스레드가 아닌 곳에서 올라온다.</summary>
    public event EventHandler<StorageFailure>? Failed;

    public TimeSpan Delay { get; }

    /// <summary>같은 <paramref name="key"/>(보통 대상 파일 경로)의 앞선 예약을 대체한다.</summary>
    public void Schedule(string key, Func<StorageFailure?> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(write);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pending.TryGetValue(key, out var existing))
            {
                existing.Write = write;
                existing.Timer.Change(Delay, Timeout.InfiniteTimeSpan);
                return;
            }

            var pending = new Pending { Write = write };
            pending.Timer = new Timer(_ => Fire(key), null, Delay, Timeout.InfiniteTimeSpan);
            _pending[key] = pending;
        }
    }

    /// <summary>밀린 쓰기를 지금 모두 수행한다(동기). 종료 직전에 호출.</summary>
    public void Flush()
    {
        List<Pending> due;
        lock (_gate)
        {
            due = [.. _pending.Values];
            foreach (var pending in due)
            {
                pending.Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _pending.Clear();
        }

        foreach (var pending in due)
        {
            Run(pending);
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    public void Dispose()
    {
        Flush();

        List<Pending> leftovers;
        lock (_gate)
        {
            _disposed = true;
            leftovers = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var pending in leftovers)
        {
            pending.Timer.Dispose();
        }
    }

    private void Fire(string key)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.Remove(key, out pending))
            {
                return;
            }
        }

        Run(pending);
    }

    private void Run(Pending pending)
    {
        StorageFailure? failure;
        try
        {
            failure = pending.Write();
        }
        catch (Exception ex)
        {
            failure = new StorageFailure(StorageFailureKind.IoError, "?", ex.Message, ex);
        }
        finally
        {
            pending.Timer.Dispose();
        }

        if (failure is not null)
        {
            Failed?.Invoke(this, failure);
        }
    }

    private sealed class Pending
    {
        public required Func<StorageFailure?> Write { get; set; }

        public Timer Timer { get; set; } = null!;
    }
}
