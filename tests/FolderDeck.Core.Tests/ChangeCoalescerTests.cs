using FolderDeck.Core.Watching;

namespace FolderDeck.Core.Tests;





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



        Assert.Equal(2, fired);


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


        clock.Advance(ChangeCoalescer.MaxWait);
        coalescer.Signal();

        Assert.Equal(1, fired);
        Assert.Equal(ChangeCoalescer.TrailingWindow, timer.Armed);

        clock.Advance(ChangeCoalescer.TrailingWindow);
        timer.Elapse();

        Assert.Equal(2, fired);
    }






    [Fact]
    public void NothingFiresWhileSuppressedAndTheSwallowedOnesNeverComeBack()
    {
        var clock = new FakeClock();
        var timer = new FakeTrailingTimer();
        var suppressed = true;
        using var coalescer = new ChangeCoalescer(clock.Now, timer, () => suppressed);
        var fired = 0;
        coalescer.Due += (_, _) => fired++;


        var step = TimeSpan.FromMilliseconds(100);
        for (var elapsed = TimeSpan.Zero; elapsed <= ChangeCoalescer.MaxWait * 3; elapsed += step)
        {
            coalescer.Signal();
            clock.Advance(step);
        }


        coalescer.SignalNow();

        clock.Advance(ChangeCoalescer.TrailingWindow);

        Assert.Equal(0, fired);


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
