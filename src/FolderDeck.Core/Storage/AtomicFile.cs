using System.Collections.Concurrent;
using System.Text;

namespace FolderDeck.Core.Storage;



















public static class AtomicFile
{
    public const string TempSuffix = ".tmp";

    private static readonly ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static void WriteAllText(string targetPath, string contents)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        lock (Gates.GetOrAdd(Path.GetFullPath(targetPath), _ => new object()))
        {
            Write(targetPath, contents);
        }
    }

    private static void Write(string targetPath, string contents)
    {
        var tempPath = targetPath + TempSuffix;


        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.WriteThrough))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }


        File.Move(tempPath, targetPath, overwrite: true);
    }
}
