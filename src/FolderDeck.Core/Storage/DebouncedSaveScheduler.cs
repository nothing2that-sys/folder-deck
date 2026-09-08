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

                // 진행 중이면 타이머를 다시 걸지 않는다 (D101). 돌고 있는 Run 이 끝날 때
                // 이 새 내용을 이어서 쓴다. 타이머를 걸면 같은 키에 쓰기가 둘 살게 되고
                // 그것이 D100 이 무는 결함이다.
                if (existing.Running)
                {
                    existing.Dirty = true;
                }
                else
                {
                    existing.Timer.Change(Delay, Timeout.InfiniteTimeSpan);
                }

                return;
            }

            var pending = new Pending { Write = write };
            pending.Timer = new Timer(_ => Fire(key), null, Delay, Timeout.InfiniteTimeSpan);
            _pending[key] = pending;
        }
    }

    /// <summary>밀린 쓰기를 지금 모두 수행한다(동기). 종료 직전에 호출.</summary>
    /// <remarks>
    /// 진행 중인 항목은 사전에서 안 빠진다(D101) — 이미 <c>Running</c> 이 세워져
    /// <see cref="Fire"/>·이전 <see cref="Flush"/> 호출이 claim 해 둔 것이다. 그래서
    /// 여기서는 <b>진행 중이 아닌 것만</b> 집어 새로 claim 하고, 진행 중인 것은 그냥
    /// <see cref="_idle"/> 로 기다린다 — <see cref="Run"/> 의 고리가 <see
    /// cref="Pending.Dirty"/> 를 다 갚은 뒤에야 <see cref="_running"/> 을 줄이므로,
    /// 이 대기는 진행 중에 이어서 쓴 것까지 포함해 기다리는 셈이 된다.
    /// </remarks>
    public void Flush()
    {
        List<KeyValuePair<string, Pending>> due;
        lock (_gate)
        {
            due = [.. _pending.Where(p => !p.Value.Running)];
            foreach (var (_, pending) in due)
            {
                pending.Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                pending.Running = true;
                pending.Dirty = false;
            }

            // claim(_running 증가)은 집은 그 잠금 안에서 한다 (D27). 잠금 밖으로 내면
            // Fire() 의 claim 과 이 메서드 사이에 창이 다시 생긴다.
            _running += due.Count;
            if (_running > 0)
            {
                _idle.Reset();
            }
        }

        foreach (var (key, pending) in due)
        {
            Run(key, pending);
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
        // _disposed 를 맨 앞에서 세운다 (D29). 먼저 세워야 그 뒤 Schedule() 이 막혀
        // 새 claim 이 생기지 않고, 새 claim 이 없어야 아래 Flush() 가 기존 claim 을 다
        // 기다린 뒤의 _idle.Dispose() 에 늦은 Set() 이 오지 않는다. Flush() 뒤에 세우면
        // 그 틈으로 들어온 Schedule() 의 타이머가 발화해 처분된 _idle 을 건드린다.
        // 덤으로 두 번 불러도 두 번째는 여기서 바로 돌아간다.
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Flush();

        List<Pending> leftovers;
        lock (_gate)
        {
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
            // 키를 빼지 않는다 (D101). 빼면 뒤에 온 Schedule 이 갈아 끼울 대상을 못 찾아
            // 둘째 쓰기를 만든다 — D100 이 무는 그 결함이다. 항목을 빼는 것은 Run 의
            // 완료 경로 하나뿐이다.
            if (!_pending.TryGetValue(key, out pending) || pending.Running)
            {
                return;
            }

            // Running 을 세운 이 잠금 안에서 claim 한다 (D27). 잠금을 놓고 증가하면
            // 그 틈에 들어온 Flush() 가 _running == 0 을 보고 지나간다.
            pending.Running = true;
            pending.Dirty = false;
            _running++;
            _idle.Reset();
        }

        Run(key, pending);
    }

    private void Run(string key, Pending pending)
    {
        while (true)
        {
            Func<StorageFailure?> write;
            lock (_gate)
            {
                write = pending.Write;
                pending.Dirty = false;
            }

            StorageFailure? failure;
            try
            {
                failure = write();
            }
            catch (Exception ex)
            {
                failure = new StorageFailure(StorageFailureKind.IoError, "?", ex.Message, ex);
            }

            if (failure is not null)
            {
                Failed?.Invoke(this, failure);
            }

            lock (_gate)
            {
                // 돌던 중 새 내용이 들어왔다 — 이어서 쓴다 (D101). 여기서 놓아 버리면
                // 진행 중에 들어온 내용이 사라진다.
                if (pending.Dirty)
                {
                    continue;
                }

                // 항목을 빼는 유일한 자리다 (D101 결정 4).
                _pending.Remove(key);
                pending.Running = false;
                pending.Timer.Dispose();
                if (--_running == 0)
                {
                    _idle.Set();
                }

                return;
            }
        }
    }

    private sealed class Pending
    {
        public required Func<StorageFailure?> Write { get; set; }

        public Timer Timer { get; set; } = null!;

        /// <summary>이 항목의 쓰기가 지금 <see cref="Run"/> 안에 들어와 있다 (D101).</summary>
        public bool Running { get; set; }

        /// <summary>진행 중에 새 내용이 들어왔다 — <see cref="Run"/> 이 이어서 한 번 더 쓴다 (D101).</summary>
        public bool Dirty { get; set; }
    }
}
