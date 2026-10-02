namespace FolderDeck.Core.Storage;

public sealed class DebouncedSaveScheduler : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Pending> _pending = [];

    private int _running;

    private readonly ManualResetEventSlim _idle = new(initialState: true);

    private bool _disposed;

    public DebouncedSaveScheduler(TimeSpan? delay = null)
    {
        Delay = delay ?? TimeSpan.FromMilliseconds(400);
    }

    public event EventHandler<StorageFailure>? Failed;

    public TimeSpan Delay { get; }

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

        _idle.Wait();
    }

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

        _idle.Dispose();
    }

    private void Fire(string key)
    {
        Pending? pending;
        lock (_gate)
        {

            if (!_pending.TryGetValue(key, out pending) || pending.Running)
            {
                return;
            }

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

                if (pending.Dirty)
                {
                    continue;
                }

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

        public bool Running { get; set; }

        public bool Dirty { get; set; }
    }
}
