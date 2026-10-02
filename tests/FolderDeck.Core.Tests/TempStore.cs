using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

internal sealed class TempStore : IDisposable
{
    private readonly List<StorageFailure> _saveFailures = [];

    public TempStore(TimeSpan? debounce = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "FolderDeck.Tests", Guid.NewGuid().ToString("N"));
        Paths = new FolderDeckPaths(Root);
        Paths.EnsureCreated();
        Scheduler = new DebouncedSaveScheduler(debounce ?? TimeSpan.FromMilliseconds(50));
        Store = new WorkspaceStore(Paths, Scheduler);
        Store.SaveFailed += (_, failure) =>
        {
            lock (_saveFailures)
            {
                _saveFailures.Add(failure);
            }
        };
    }

    public string Root { get; }

    public FolderDeckPaths Paths { get; }

    public DebouncedSaveScheduler Scheduler { get; }

    public WorkspaceStore Store { get; }

    public IReadOnlyList<StorageFailure> SaveFailures
    {
        get
        {
            lock (_saveFailures)
            {
                return [.. _saveFailures];
            }
        }
    }

    public string WriteRawWorkspace(string fileName, string json)
    {
        var path = Path.Combine(Paths.WorkspacesDir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        Store.Dispose();
        Scheduler.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {

        }
    }
}
