using System.Threading.Channels;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Enumeration;

public sealed class FolderEnumerator : IFolderEnumerator
{
    public Task<FolderListing> ListAsync(
        string path,
        SortBy sortBy = SortBy.Name,
        bool sortDesc = false,
        bool foldersFirst = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(
            () => List(path, sortBy, sortDesc, foldersFirst, cancellationToken), cancellationToken);
    }

    public Task<FolderAccessFailure?> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(
            () =>
            {
                try
                {

                    using var probe = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator();
                    probe.MoveNext();
                    return null as FolderAccessFailure;
                }
                catch (Exception ex)
                {
                    return Describe(ex, path);
                }
            },
            cancellationToken);
    }

    public IAsyncEnumerable<FolderSearchHit> SearchAsync(
        string root,
        string query,
        FolderSearchStats stats,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(stats);

        var channel = Channel.CreateBounded<FolderSearchHit>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await WalkAsync(channel.Writer, root, query, stats, cancellationToken)
                        .ConfigureAwait(false);
                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException)
                {
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            },
            CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private static async Task WalkAsync(
        ChannelWriter<FolderSearchHit> writer,
        string root,
        string query,
        FolderSearchStats stats,
        CancellationToken ct)
    {

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = [.. new DirectoryInfo(current).EnumerateFileSystemInfos()];
                stats.CountScanned();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {

                stats.CountSkipped();
                continue;
            }

            foreach (var info in entries)
            {
                ct.ThrowIfCancellationRequested();

                bool isDirectory;
                bool isLink;
                FolderItem item;
                try
                {
                    isDirectory = (info.Attributes & FileAttributes.Directory) != 0;

                    isLink = info.LinkTarget is not null;

                    item = new FolderItem(
                        info.Name,
                        info.FullName,
                        isDirectory,
                        isDirectory ? null : (info as FileInfo)?.Length,
                        info.LastWriteTimeUtc);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (isDirectory && !isLink)
                {
                    pending.Push(info.FullName);
                }

                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteAsync(
                        new FolderSearchHit(item, RelativeFolder(root, current)), ct).ConfigureAwait(false);
                }
            }
        }
    }

    private static string RelativeFolder(string root, string folder)
    {
        if (string.Equals(root, folder, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(root, folder);
        return relative == "." ? string.Empty : relative;
    }

    private static FolderListing List(
        string path, SortBy sortBy, bool sortDesc, bool foldersFirst, CancellationToken ct)
    {
        var items = new List<FolderItem>();

        try
        {

            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                    items.Add(new FolderItem(
                        info.Name,
                        info.FullName,
                        isDirectory,
                        isDirectory ? null : (info as FileInfo)?.Length,
                        info.LastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {

                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FolderListing.Fail(Describe(ex, path));
        }

        ct.ThrowIfCancellationRequested();
        Sort(items, sortBy, sortDesc, foldersFirst);
        return FolderListing.Ok(path, items);
    }

    private static void Sort(List<FolderItem> items, SortBy sortBy, bool sortDesc, bool foldersFirst)
    {
        items.Sort((a, b) =>
        {
            if (foldersFirst && a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            var result = sortBy switch
            {
                SortBy.Modified => a.ModifiedUtc.CompareTo(b.ModifiedUtc),
                SortBy.Size => Nullable.Compare(a.Size, b.Size),
                SortBy.Type => string.Compare(
                    Path.GetExtension(a.Name), Path.GetExtension(b.Name), StringComparison.OrdinalIgnoreCase),
                _ => 0,
            };

            if (sortDesc)
            {
                result = -result;
            }

            return result != 0
                ? result
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) * (sortDesc ? -1 : 1);
        });
    }

    private static FolderAccessFailure Describe(Exception ex, string path) => ex switch
    {
        UnauthorizedAccessException => new FolderAccessFailure(
            FolderAccessFailureKind.AccessDenied, path, "권한이 없어 열 수 없다.", ex),

        DirectoryNotFoundException => new FolderAccessFailure(
            FolderAccessFailureKind.NotFound, path, "경로가 없다.", ex),

        IOException => new FolderAccessFailure(
            FolderAccessFailureKind.Unavailable, path, ex.Message, ex),

        _ => new FolderAccessFailure(FolderAccessFailureKind.IoError, path, ex.Message, ex),
    };
}
