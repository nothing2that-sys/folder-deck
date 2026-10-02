using System.IO;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Services;

internal static class CrashLogger
{

    internal static void TryWrite(string source, Exception exception)
    {
        try
        {
            var dir = Path.Combine(FolderDeckPaths.Default.Root, "crashes");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyy-MM-dd_HHmmss_fff}.log");
            var text =
                $"시각: {DateTime.Now:O}\n" +
                $"자리: {source}\n" +
                $"판: {typeof(CrashLogger).Assembly.GetName().Version}\n\n" +
                exception;

            File.WriteAllText(path, text);
        }
        catch
        {

        }
    }
}
