namespace FolderDeck.Core.Watching;

/// <summary>
/// 바깥에서 온 변경 신호를 갱신 발화로 접는 부품 (§8 · Phase 5).
/// </summary>
/// <remarks>
/// <b>파일 시스템도 UI 도 모른다.</b> 아는 것은 <see cref="TrailingWindow"/>·
/// <see cref="MaxWait"/>·시계뿐이라 실시간을 기다리지 않고 시험할 수 있다 —
/// 실제 <c>FileSystemWatcher</c> 로 시험하면 플레이크만 는다.
/// <para>
/// <b>무엇이 바뀌었는지 담지 않는다.</b> 갱신이 어차피 전량 재조회라 이벤트 인자를
/// 쌓을 이유가 없고, 신호를 세지도 않는다.
/// </para>
/// </remarks>
public sealed class ChangeCoalescer : IDisposable
{
    /// <summary>
    /// 트레일링 창. 조작 하나가 이벤트 1~4건을 거의 동시에 내므로(프로브 실측)
    /// 이만큼 조용해질 때까지 기다리면 조작 하나가 갱신 1회로 접힌다.
    /// </summary>
    public static readonly TimeSpan TrailingWindow = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 한 다발이 이만큼 이어지면 조용해지길 기다리지 않고 발화한다. 대량 복사가 도는
    /// 동안에는 <see cref="TrailingWindow"/> 만큼의 침묵이 <b>한 번도 오지 않는다</b>
    /// (실측: 1,000개 복사가 로컬 655ms · 원격 12,958ms · 탐색기 로컬 13,407ms 동안
    /// 끊이지 않았다). 이 값이 없으면 그 13초 내내 목록이 멈춰 있다.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(1000);

    private readonly Lock _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly ITrailingTimer _timer;

    /// <summary>
    /// 켜져 있으면 발화를 삼킨다. <b>무엇이 켜는지는 이 부품이 모른다</b> — 판정을 통째로
    /// 밖에서 받는다(§8 · Phase 5 수정 1: 앱 자신의 파일 조작).
    /// </summary>
    private readonly Func<bool>? _suppressed;

    /// <summary>이번 다발의 첫 신호 시각. 다발이 없으면 null.</summary>
    private DateTimeOffset? _burstStart;

    private bool _disposed;

    /// <summary>기본값은 실시계와 실타이머다. 시험은 둘 다 갈아끼운다.</summary>
    /// <param name="suppressed">
    /// 발화를 삼킬 조건. 주지 않으면 아무것도 삼키지 않는다. <b>스레드가 정해져 있지 않은
    /// 자리에서 불린다</b> — 값 하나를 읽어 돌려주는 것 이상을 하면 안 된다.
    /// </param>
    public ChangeCoalescer(
        Func<DateTimeOffset>? clock = null,
        ITrailingTimer? timer = null,
        Func<bool>? suppressed = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _timer = timer ?? new SystemTrailingTimer();
        _suppressed = suppressed;
        _timer.Elapsed += OnTrailingElapsed;
    }

    /// <summary>갱신할 때가 됐다. <b>스레드는 정해져 있지 않다</b> — 받는 쪽이 마샬한다.</summary>
    public event EventHandler? Due;

    /// <summary>보통 신호 — 트레일링을 다시 걸되, 다발이 <see cref="MaxWait"/> 를 넘겼으면 바로 발화한다.</summary>
    public void Signal()
    {
        bool now;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var stamp = _clock();
            _burstStart ??= stamp;

            now = stamp - _burstStart.Value >= MaxWait;

            if (now)
            {
                // 발화하면 다발이 끝난다. 다음 신호가 새 다발의 첫 신호다.
                _burstStart = null;
                _timer.Stop();
            }
            else
            {
                _timer.Restart(TrailingWindow);
            }
        }

        if (now)
        {
            Fire();
        }
    }

    /// <summary>
    /// 즉시 신호 — 기다리지 않고 그 자리에서 발화한다. <c>Error</c>(버퍼 초과)가 이 자리다:
    /// 몇 건을 삼켰는지 알 방법이 없어 전량 재조회로만 갚는다.
    /// </summary>
    public void SignalNow()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _burstStart = null;
            _timer.Stop();
        }

        Fire();
    }

    /// <summary>대기 중인 다발을 발화 없이 접는다 — 감시 대상 경로가 바뀔 때 쓴다.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _burstStart = null;
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _burstStart = null;
            _timer.Stop();
        }

        _timer.Elapsed -= OnTrailingElapsed;
        _timer.Dispose();
    }

    private void OnTrailingElapsed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _burstStart = null;
        }

        Fire();
    }

    // 자물쇠 밖에서 부른다 — 받는 쪽이 무엇을 하는지 여기서는 모른다.
    //
    // 삼킴은 <b>여기</b>다. 받는 쪽이 UI 스레드로 마샬하기 전이라 삼킨 발화는 디스패처를
    // 건드리지도 않는다 — 큐에 올린 뒤 UI 스레드에서 버리면 이미 값을 치른 뒤다.
    // 쌓아 두지 않는다: 이 자리에 온 시점에 다발 상태는 이미 접혀 있고, 삼킨 것을
    // 나중에 몰아 터뜨리는 통로가 없다.
    private void Fire()
    {
        if (_suppressed?.Invoke() == true)
        {
            return;
        }

        Due?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 트레일링 타이머. <see cref="ChangeCoalescer"/> 를 실시간 대기 없이 시험하려고
/// 이 한 자리만 갈아끼운다.
/// </summary>
public interface ITrailingTimer : IDisposable
{
    /// <summary>이미 걸려 있던 것을 버리고 <paramref name="delay"/> 뒤로 다시 건다.</summary>
    void Restart(TimeSpan delay);

    /// <summary>걸려 있던 것을 접는다. 안 걸려 있으면 아무 일도 없다.</summary>
    void Stop();

    event EventHandler? Elapsed;
}

/// <summary>실시계 타이머. 콜백은 스레드 풀에서 올라온다.</summary>
public sealed class SystemTrailingTimer : ITrailingTimer
{
    private readonly Timer _timer;

    public SystemTrailingTimer()
    {
        _timer = new Timer(_ => Elapsed?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Elapsed;

    public void Restart(TimeSpan delay) => _timer.Change(delay, Timeout.InfiniteTimeSpan);

    public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Dispose() => _timer.Dispose();
}
