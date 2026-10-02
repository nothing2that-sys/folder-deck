using System.Diagnostics;

namespace FolderDeck.App.Services;

public static class InstanceNames
{

    public static int SessionId { get; } = ReadSessionId();

    public static string WorkspaceMutex(Guid workspaceId) => $@"Local\FolderDeck.{workspaceId}";

    public static string WorkspacePipe(Guid workspaceId) => Pipe(workspaceId.ToString());

    private static string Pipe(string key) => $"FolderDeck.s{SessionId}.{key}";

    private static int ReadSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception)
        {

            return 0;
        }
    }
}
