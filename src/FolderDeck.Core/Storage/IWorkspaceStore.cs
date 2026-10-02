using FolderDeck.Core.Models;

namespace FolderDeck.Core.Storage;

public sealed record WorkspaceListing(
    IReadOnlyList<Workspace> Workspaces,
    IReadOnlyList<StorageFailure> Failures);

public interface IWorkspaceStore
{
    FolderDeckPaths Paths { get; }

    event EventHandler<StorageFailure>? SaveFailed;

    WorkspaceListing ListWorkspaces();

    StorageResult<Workspace> LoadWorkspace(Guid id);

    StorageResult<Workspace> LoadWorkspaceFile(string filePath);

    StorageFailure? SaveWorkspace(Workspace workspace);

    void SaveWorkspaceDebounced(Workspace workspace);

    StorageFailure? DeleteWorkspace(Guid id);

    StorageResult<AppSettings> LoadSettings();

    StorageFailure? SaveSettings(AppSettings settings);

    void SaveSettingsDebounced(AppSettings settings);

    void Flush();
}
