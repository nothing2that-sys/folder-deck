using System.Text.Json;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Storage;

public sealed class WorkspaceStore : IWorkspaceStore, IDisposable
{
    private readonly DebouncedSaveScheduler _scheduler;
    private readonly bool _ownsScheduler;

    public WorkspaceStore(FolderDeckPaths? paths = null, DebouncedSaveScheduler? scheduler = null)
    {
        Paths = paths ?? FolderDeckPaths.Default;
        _ownsScheduler = scheduler is null;
        _scheduler = scheduler ?? new DebouncedSaveScheduler();
        _scheduler.Failed += OnSchedulerFailed;
    }

    public event EventHandler<StorageFailure>? SaveFailed;

    public FolderDeckPaths Paths { get; }

    public WorkspaceListing ListWorkspaces()
    {
        var workspaces = new List<Workspace>();
        var failures = new List<StorageFailure>();

        string[] files;
        try
        {
            if (!Directory.Exists(Paths.WorkspacesDir))
            {
                return new WorkspaceListing(workspaces, failures);
            }

            files = [.. Directory.EnumerateFiles(Paths.WorkspacesDir, "*.json")
                .Where(f => string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            failures.Add(Describe(ex, Paths.WorkspacesDir));
            return new WorkspaceListing(workspaces, failures);
        }

        foreach (var file in files)
        {
            var result = LoadWorkspaceFile(file);
            if (result.Value is not null)
            {
                workspaces.Add(result.Value);
            }
            else
            {
                failures.Add(result.Failure!);
            }
        }

        return new WorkspaceListing(workspaces, failures);
    }

    public StorageResult<Workspace> LoadWorkspace(Guid id) => LoadWorkspaceFile(Paths.WorkspaceFile(id));

    public StorageResult<Workspace> LoadWorkspaceFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var read = ReadJson<Workspace>(filePath);
        if (read.Value is null)
        {
            return read;
        }

        var workspace = read.Value;

        if (workspace.SchemaVersion > Workspace.CurrentSchemaVersion)
        {
            return Invalid<Workspace>(
                StorageFailureKind.UnsupportedSchemaVersion,
                filePath,
                $"schemaVersion {workspace.SchemaVersion} 은(는) 이 버전이 모르는 형식이다 " +
                $"(지원: {Workspace.CurrentSchemaVersion}).");
        }

        if (workspace.SchemaVersion < 1)
        {
            return Invalid<Workspace>(StorageFailureKind.InvalidContent, filePath, "schemaVersion 이 없거나 잘못됐다.");
        }

        if (workspace.Id == Guid.Empty)
        {
            return Invalid<Workspace>(StorageFailureKind.InvalidContent, filePath, "id 가 비었다.");
        }

        var expectedName = $"{workspace.Id}.json";
        var actualName = Path.GetFileName(filePath);
        if (!string.Equals(expectedName, actualName, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid<Workspace>(
                StorageFailureKind.InvalidContent,
                filePath,
                $"파일 이름과 id 가 다르다 (id 기준 이름: {expectedName}).");
        }

        workspace.Folders ??= [];

        var seenFolderIds = new HashSet<Guid>();
        foreach (var folder in workspace.Folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path))
            {
                return Invalid<Workspace>(
                    StorageFailureKind.InvalidContent, filePath, "path 가 빈 folders 항목이 있다.");
            }

            if (folder.Id == Guid.Empty)
            {
                return Invalid<Workspace>(
                    StorageFailureKind.InvalidContent, filePath, $"id 가 빈 folders 항목이 있다 (path: {folder.Path}).");
            }

            if (!seenFolderIds.Add(folder.Id))
            {
                return Invalid<Workspace>(
                    StorageFailureKind.InvalidContent, filePath, $"folders 안에 중복된 id 가 있다: {folder.Id}.");
            }
        }

        if (workspace.Tiles is not null)
        {
            var firstSearchIndex = workspace.Tiles.FindIndex(t => t.Kind == TileKind.Search);
            if (firstSearchIndex >= 0)
            {
                workspace.Tiles[firstSearchIndex].FolderId = null;

                for (var i = workspace.Tiles.Count - 1; i > firstSearchIndex; i--)
                {
                    if (workspace.Tiles[i].Kind == TileKind.Search)
                    {
                        workspace.Tiles.RemoveAt(i);
                    }
                }
            }
        }

        return StorageResult<Workspace>.Ok(workspace);
    }

    public StorageFailure? SaveWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Id == Guid.Empty)
        {
            throw new ArgumentException("워크스페이스 id 가 비었다.", nameof(workspace));
        }

        var path = Paths.WorkspaceFile(workspace.Id);
        workspace.SchemaVersion = Workspace.CurrentSchemaVersion;
        var json = Serialize(workspace, out var failure);
        return failure ?? WriteJson(path, json!);
    }

    public void SaveWorkspaceDebounced(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Id == Guid.Empty)
        {
            throw new ArgumentException("워크스페이스 id 가 비었다.", nameof(workspace));
        }

        var path = Paths.WorkspaceFile(workspace.Id);
        workspace.SchemaVersion = Workspace.CurrentSchemaVersion;

        var json = Serialize(workspace, out var failure);
        if (failure is not null)
        {
            SaveFailed?.Invoke(this, failure);
            return;
        }

        _scheduler.Schedule(path, () => WriteJson(path, json!));
    }

    public StorageFailure? DeleteWorkspace(Guid id)
    {

        var path = Paths.WorkspaceFile(id);
        try
        {
            if (!File.Exists(path))
            {
                return new StorageFailure(StorageFailureKind.NotFound, path, "지울 워크스페이스 파일이 없다.");
            }

            File.Delete(path);
            return null;
        }
        catch (Exception ex)
        {
            return Describe(ex, path);
        }
    }

    public StorageResult<AppSettings> LoadSettings()
    {
        var read = ReadJson<AppSettings>(Paths.SettingsFile);
        if (read.Value is null)
        {
            return read;
        }

        if (read.Value.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            return Invalid<AppSettings>(
                StorageFailureKind.UnsupportedSchemaVersion,
                Paths.SettingsFile,
                $"schemaVersion {read.Value.SchemaVersion} 은(는) 이 버전이 모르는 형식이다 " +
                $"(지원: {AppSettings.CurrentSchemaVersion}).");
        }

        if (read.Value.SchemaVersion < 1)
        {
            return Invalid<AppSettings>(
                StorageFailureKind.InvalidContent, Paths.SettingsFile, "schemaVersion 이 없거나 잘못됐다.");
        }

        return read;
    }

    public StorageFailure? SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        var json = Serialize(settings, out var failure);
        return failure ?? WriteJson(Paths.SettingsFile, json!);
    }

    public void SaveSettingsDebounced(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        var json = Serialize(settings, out var failure);
        if (failure is not null)
        {
            SaveFailed?.Invoke(this, failure);
            return;
        }

        _scheduler.Schedule(Paths.SettingsFile, () => WriteJson(Paths.SettingsFile, json!));
    }

    public void Flush() => _scheduler.Flush();

    public void Dispose()
    {
        _scheduler.Failed -= OnSchedulerFailed;
        if (_ownsScheduler)
        {
            _scheduler.Dispose();
        }
        else
        {
            _scheduler.Flush();
        }
    }

    private static string? Serialize<T>(T value, out StorageFailure? failure)
    {
        try
        {
            failure = null;
            return JsonSerializer.Serialize(value, FolderDeckJson.Options);
        }
        catch (Exception ex)
        {
            failure = new StorageFailure(StorageFailureKind.InvalidContent, "?", $"직렬화 실패: {ex.Message}", ex);
            return null;
        }
    }

    private static StorageResult<T> ReadJson<T>(string filePath)
        where T : class
    {
        string text;
        try
        {
            if (!File.Exists(filePath))
            {
                return StorageResult<T>.Fail(
                    new StorageFailure(StorageFailureKind.NotFound, filePath, "파일이 없다."));
            }

            text = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return StorageResult<T>.Fail(Describe(ex, filePath));
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(text, FolderDeckJson.Options);
            if (value is null)
            {
                return StorageResult<T>.Fail(
                    new StorageFailure(StorageFailureKind.CorruptData, filePath, "내용이 비었거나 null 이다."));
            }

            return StorageResult<T>.Ok(value);
        }
        catch (JsonException ex)
        {

            return StorageResult<T>.Fail(
                new StorageFailure(StorageFailureKind.CorruptData, filePath, $"JSON 을 읽을 수 없다: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return StorageResult<T>.Fail(
                new StorageFailure(StorageFailureKind.CorruptData, filePath, ex.Message, ex));
        }
    }

    private static StorageResult<T> Invalid<T>(StorageFailureKind kind, string path, string message)
        where T : class
        => StorageResult<T>.Fail(new StorageFailure(kind, path, message));

    private static StorageFailure? WriteJson(string path, string json)
    {
        try
        {
            AtomicFile.WriteAllText(path, json);
            return null;
        }
        catch (Exception ex)
        {
            return Describe(ex, path);
        }
    }

    private static StorageFailure Describe(Exception ex, string path) => ex switch
    {
        UnauthorizedAccessException => new StorageFailure(StorageFailureKind.AccessDenied, path, ex.Message, ex),
        FileNotFoundException => new StorageFailure(StorageFailureKind.NotFound, path, ex.Message, ex),
        DirectoryNotFoundException => new StorageFailure(StorageFailureKind.NotFound, path, ex.Message, ex),
        _ => new StorageFailure(StorageFailureKind.IoError, path, ex.Message, ex),
    };

    private void OnSchedulerFailed(object? sender, StorageFailure failure) => SaveFailed?.Invoke(this, failure);
}
