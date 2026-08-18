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

    /// <summary>
    /// 지금 <see cref="Run"/> 안에 들어와 있는 쓰기의 수 (D27). <see cref="_gate"/> 로만 읽고 쓴다.
    /// </summary>
    private int _running;

    /// <summary>
    /// 진행 중인 쓰기가 하나도 없으면 set (D27). <see cref="Flush"/> 가 이것을 기다려
    /// "지금 모두 수행한다(동기)"를 지킨다.
    /// </summary>
    private readonly ManualResetEventSlim _idle = new(initialState: true);

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
    /// <remarks>
    /// 자기가 꺼낸 것만 돌고 끝내면 안 된다 (D27). 타이머가 이미 발화해 <see cref="Fire"/> 가
    /// 항목을 빼 간 뒤라면 사전이 비어 있어 돌 것이 없고, 그 쓰기는 아직 진행 중이다.
    /// 그 상태로 돌아가면 <b>호출자가 원자적 교체 전 파일을 읽는다</b> — 쓰기가 사라지는 것은
    /// 아니지만 이 메서드가 약속한 동기성이 깨진다. 그래서 <see cref="_running"/> 이 0 이
    /// 될 때까지 기다린다.
    /// </remarks>
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

            // claim(_running 증가)은 사전을 비운 그 잠금 안에서 한다. 잠금 밖으로 내면
            // Fire() 의 Remove 와 증가 사이에 이 메서드가 끼어들어 빈 사전 + _running == 0 을
            // 보고 그냥 돌아가는 창이 다시 생긴다 — 지금 고치는 그 결함과 똑같은 창이다.
            _running += due.Count;
            if (_running > 0)
            {
                _idle.Reset();
            }
        }

        foreach (var pending in due)
        {
            Run(pending);
        }

        // 잠금 밖에서 기다린다. 물고 기다리면 Run() 의 감소가 들어올 수 없어 굳는다.
        _idle.Wait();
    }

    /// <summary>
    /// 밀린 쓰기가 있는가. <b>예약된 것뿐 아니라 진행 중인 것도 센다</b> (D27) —
    /// <see cref="Fire"/> 가 키를 빼 간 뒤 쓰는 중인 것을 "없다"고 답하면 안 된다.
    /// </summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0 || _running > 0;
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

        // 맨 끝이다 — 위의 Flush() 가 이것을 기다린 뒤, 남은 타이머까지 처분한 뒤다.
        _idle.Dispose();
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

            // Remove 가 성공한 이 잠금 안에서 claim 한다 (D27). 잠금을 놓고 증가하면
            // 그 틈에 들어온 Flush() 가 빈 사전 + _running == 0 을 보고 지나간다.
            _running++;
            _idle.Reset();
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

            lock (_gate)
            {
                if (--_running == 0)
                {
                    _idle.Set();
                }
            }
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
