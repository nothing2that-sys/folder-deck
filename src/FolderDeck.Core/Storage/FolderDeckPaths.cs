namespace FolderDeck.Core.Storage;

public sealed class FolderDeckPaths
{
    public const string WorkspacesDirName = "workspaces";
    public const string SettingsFileName = "settings.json";

    public FolderDeckPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public static FolderDeckPaths Default { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FolderDeck"));

    public string Root { get; }

    public string WorkspacesDir => Path.Combine(Root, WorkspacesDirName);

    public string SettingsFile => Path.Combine(Root, SettingsFileName);

    public string WorkspaceFile(Guid id) => Path.Combine(WorkspacesDir, $"{id}.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WorkspacesDir);
    }
}
