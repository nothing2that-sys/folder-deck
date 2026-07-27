using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>실제 <c>%APPDATA%</c> 를 건드리지 않는 일회용 저장소 루트.</summary>
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

    /// <summary>워크스페이스 파일을 손으로 써 넣는다(손상된 내용 포함).</summary>
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
            // 테스트 정리 실패는 무시.
        }
    }
}
