using FolderDeck.Core.Watching;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 합치기 부품(§8 · Phase 5)의 시간 규칙. <b>시계도 타이머도 가짜다</b> —
/// <c>Thread.Sleep</c> 도, 실제 <c>FileSystemWatcher</c> 도 쓰지 않는다.
/// </summary>
public class ChangeCoalescerTests
{
    [Fact]
    public void OneSignalFiresOnceAfterTheTrailingWindow()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        using var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        coalescer.Signal();

        // 아직 아니다 — 트레일링이 걸렸을 뿐이다.
        Assert.Equal(ChangeCoalescer.TrailingWindow, timer.Armed);
        Assert.Equal(0, fired);

        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(1, fired);
        Assert.Null(timer.Armed);
    }

    [Fact]
    public void FourSignalsInsideTheWindowFireOnce()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        using var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        // 조작 하나가 내는 최대 건수다(원격 File.Copy = 4건, 거의 동시에 온다).
        for (var i = 0; i < 4; i++)
        {
            coalescer.Signal();
            clock.Advance(TimeSpan.FromMilliseconds(5));
        }

        Assert.Equal(0, fired);

        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void AnUnbrokenStreamStillFiresEveryMaxWait()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        using var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        // 대량 복사가 도는 동안에는 트레일링만큼의 침묵이 한 번도 오지 않는다.
        var step = TimeSpan.FromMilliseconds(100);
        var stream = ChangeCoalescer.MaxWait * 3;

        for (var elapsed = TimeSpan.Zero; elapsed <= stream; elapsed += step)
        {
            coalescer.Signal();

            if (elapsed < stream)
            {
                clock.Advance(step);
            }
        }

        // 다발 중에 두 번 — 1000ms 와 2100ms 에서. (발화 뒤 다발이 새로 시작하므로
        // 신호 간격만큼 밀린다.)
        Assert.Equal(2, fired);

        // 다발이 끝나면 트레일링이 마지막 한 번을 갚는다.
        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(3, fired);
    }

    [Fact]
    public void ImmediateSignalFiresAtOnceAndFoldsThePendingTimer()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        using var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        coalescer.Signal();
        Assert.NotNull(timer.Armed);

        // Error(버퍼 초과)가 이 자리다. 몇 건을 삼켰는지 모르므로 기다리지 않는다.
        coalescer.SignalNow();

        Assert.Equal(1, fired);
        Assert.Null(timer.Armed);
    }

    [Fact]
    public void ANewSignalAfterFiringStartsAFreshBurst()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        using var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        coalescer.Signal();
        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();
        Assert.Equal(1, fired);

        // 첫 다발의 시작 시각을 물려받았다면 여기서 곧바로 또 발화한다(MaxWait 를 넘겼으므로).
        clock.Advance(ChangeCoalescer.MaxWait);
        coalescer.Signal();

        Assert.Equal(1, fired);
        Assert.Equal(ChangeCoalescer.TrailingWindow, timer.Armed);

        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(2, fired);
    }

    /// <summary>
    /// 억제가 켜진 동안에는 발화가 나가지 않는다 — 앱이 스스로 파일을 조작하는 자리다
    /// (Phase 5 수정 1). <b>쌓아 두었다가 나중에 몰아 터뜨리지도 않는다</b>: 억제가 풀린 뒤
    /// 도는 것은 그 뒤에 온 신호 하나뿐이고, 삼킨 동안의 변경은 조작 후 갱신이 갚는다.
    /// </summary>
    [Fact]
    public void NothingFiresWhileSuppressedAndTheSwallowedOnesNeverComeBack()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        var suppressed = true;
        using var coalescer = new ChangeCoalescer(clock.Now, timer, () => suppressed);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        // 끊이지 않는 다발 — 트레일링도 MaxWait 도 둘 다 지난다.
        var step = TimeSpan.FromMilliseconds(100);
        for (var elapsed = TimeSpan.Zero; elapsed <= ChangeCoalescer.MaxWait * 3; elapsed += step)
        {
            coalescer.Signal();
            clock.Advance(step);
        }

        // 버퍼 초과(Error)의 즉시 발화도 삼킨다 — 조작 후 갱신이 어차피 전량 재조회다.
        coalescer.SignalNow();

        clock.Advance(ChangeCoalescer.TrailingWindow);

        Assert.Equal(0, fired);

        // 조작이 끝났다. 그 뒤에 온 신호 하나가 한 번 발화한다 — 삼킨 다발이 되살아나는 것이 아니다.
        suppressed = false;

        coalescer.Signal();
        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void SignalsAfterDisposeDoNothing()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        var coalescer = new ChangeCoalescer(clock.Now, timer);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;

        coalescer.Dispose();

        coalescer.Signal();
        coalescer.SignalNow();

        Assert.Equal(0, fired);
        Assert.Null(timer.Armed);
    }

    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

        public Func<DateTimeOffset> Now => () => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>건 것을 기록만 하고, 시험이 손으로 터뜨린다.</summary>
    private sealed class FakeTrailingTimer : ITrailingTimer
    {
        public TimeSpan? Armed { get; private set; }

        public event EventHandler? Elapsed;

        public void Restart(TimeSpan delay) => Armed = delay;

        public void Stop() => Armed = null;

        public void Elapse()
        {
            Assert.NotNull(Armed);
            Armed = null;
            Elapsed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Armed = null;
    }
}
