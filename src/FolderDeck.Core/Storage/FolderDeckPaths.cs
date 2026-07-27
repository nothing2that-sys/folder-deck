namespace FolderDeck.Core.Storage;

/// <summary>
/// 저장 위치 (§7). 기본 루트는 <c>%APPDATA%\FolderDeck\</c>.
/// 테스트는 임의 루트를 주입해 실제 APPDATA 를 건드리지 않는다.
/// </summary>
public sealed class FolderDeckPaths
{
    public const string WorkspacesDirName = "workspaces";
    public const string SettingsFileName = "settings.json";

    public FolderDeckPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary><c>%APPDATA%\FolderDeck\</c>.</summary>
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
