using System.Threading;

namespace FolderDeck.App.Services;

public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    private InstanceLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        _owned = true;
        Name = name;
    }

    public string Name { get; }

    public static InstanceLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(initiallyOwned: false, name);

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {

            acquired = true;
        }

        if (acquired)
        {
            return new InstanceLock(mutex, name);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_owned)
        {
            _owned = false;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {

            }
        }

        _mutex.Dispose();
    }
}
