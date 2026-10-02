namespace FolderDeck.Core.Watching;













public sealed class ChangeCoalescer : IDisposable
{




    public static readonly TimeSpan TrailingWindow = TimeSpan.FromMilliseconds(300);







    public static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(1000);

    private readonly Lock _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly ITrailingTimer _timer;





    private readonly Func<bool>? _suppressed;


    private DateTimeOffset? _burstStart;

    private bool _disposed;






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


    public event EventHandler? Due;


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







    private void Fire()
    {
        try
        {
            if (_suppressed?.Invoke() == true)
            {
                return;
            }

            Due?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {




        }
    }
}





public interface ITrailingTimer : IDisposable
{

    void Restart(TimeSpan delay);


    void Stop();

    event EventHandler? Elapsed;
}


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
