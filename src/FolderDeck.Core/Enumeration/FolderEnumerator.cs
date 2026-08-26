using System.Threading.Channels;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Enumeration;

/// <inheritdoc cref="IFolderEnumerator"/>
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

        // 파일 시스템 열거는 블로킹 I/O 다. 네트워크 경로면 몇 초씩 걸리므로
        // UI 스레드 밖으로 밀어낸다(§8 — 폴더 열거는 async).
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
                    // 항목을 하나만 당겨 본다. 실제로 열어봐야 권한·네트워크 상태를 알 수 있다.
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

        // 경계 있는 채널로 넘긴다. 걷기는 블로킹 I/O 라 스레드 풀에서 돌려야 하고,
        // 결과가 폭주해도 소비 속도에 맞춰 눌린다(Temp 1326개 · 죽은 UNC 경험).
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
        // 명시적 스택 — 깊은 트리에서 재귀 호출로 스택을 터뜨리지 않는다.
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
                // 권한 없음·끊긴 네트워크는 건너뛰고 계속한다. 멈추지 않는다(§5).
                stats.CountSkipped();
                continue;
            }

            foreach (var info in entries)
            {
                ct.ThrowIfCancellationRequested();

                bool isDirectory;
                FolderItem item;
                try
                {
                    isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
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

                if (isDirectory)
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

    /// <summary>기준 폴더를 뺀 상대 폴더. 기준 바로 밑이면 빈 문자열.</summary>
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
            // EnumerateFileSystemInfos 는 이름·크기·시각을 한 번에 준다(항목마다 다시 stat 하지 않음).
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
                    // 열거 도중에 사라졌거나 못 읽는 항목 하나 때문에 폴더 전체를 실패로
                    // 만들지 않는다. 설비가 쓰는 중인 로그 폴더에서 흔한 일이다(§5).
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

    /// <summary>
    /// <paramref name="foldersFirst"/> 면 폴더를 항상 먼저 두고(탐색기와 같은 감각), 그 안에서
    /// <paramref name="sortBy"/> 로 정렬한다. <paramref name="sortDesc"/> 는 그룹 안의 순서만
    /// 뒤집는다 — 폴더가 파일 아래로 내려가지 않는다.
    /// </summary>
    /// <remarks>
    /// <paramref name="foldersFirst"/> 가 <c>false</c> 면 그 앞머리 비교를 건너뛰어 폴더와 파일이
    /// <b>한 덩어리</b>가 된다 (Phase 8-B ②). 그때 <c>크기</c> 기준에서 폴더는 <c>Size</c> 가
    /// <c>null</c> 이라 <see cref="Nullable.Compare{T}"/> 규칙대로 <b>모든 파일보다 앞</b>에 선다 —
    /// 폴더 크기를 재귀로 재지 않기 때문이고(§2.2), 통합 정렬을 켜도 크기만은 폴더가 뭉쳐 보인다.
    /// </remarks>
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

            // 같은 값이면 이름으로 안정화한다(정렬이 실행마다 흔들리지 않게).
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

        // 네트워크 경로가 끊긴 경우 여기로 온다. 경로는 유지하고 상태만 표시한다(§5).
        IOException => new FolderAccessFailure(
            FolderAccessFailureKind.Unavailable, path, ex.Message, ex),

        _ => new FolderAccessFailure(FolderAccessFailureKind.IoError, path, ex.Message, ex),
    };
}
