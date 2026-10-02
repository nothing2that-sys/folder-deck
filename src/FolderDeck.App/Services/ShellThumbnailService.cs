using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FolderDeck.App.Interop;

namespace FolderDeck.App.Services;

public sealed class ShellThumbnailService
{
    internal const int DefaultCacheCapacity = 64;

    private static readonly SemaphoreSlim SharedFetchGate = new(2, 2);

    private readonly object _gate = new();
    private readonly int _cacheCapacity;
    private readonly Func<string, int, int, bool, BitmapSource?> _fetch;
    private readonly SemaphoreSlim _fetchGate;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();

    private sealed record CacheEntry(
        BitmapSource Image, int RequestedPixelWidth, int RequestedPixelHeight,
        LinkedListNode<string> Node);

    public ShellThumbnailService(int cacheCapacity = DefaultCacheCapacity)
        : this(cacheCapacity, Fetch, SharedFetchGate)
    {
    }

    internal ShellThumbnailService(
        int cacheCapacity,
        Func<string, int, int, bool, BitmapSource?> fetch,
        SemaphoreSlim fetchGate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(fetchGate);
        _cacheCapacity = cacheCapacity;
        _fetch = fetch;
        _fetchGate = fetchGate;
    }

    internal static string ComputeStamp(string path, long length, DateTime modifiedUtc) =>
        $"{path.ToLowerInvariant()}|{length}|{modifiedUtc:O}";

    internal bool TryGetCached(string stamp, out BitmapSource? image)
        => TryGetCached(ThumbnailKey(stamp), 0, 0, out image);

    private bool TryGetCached(
        string key, int minimumPixelWidth, int minimumPixelHeight, out BitmapSource? image)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry)
                && entry.RequestedPixelWidth >= minimumPixelWidth
                && entry.RequestedPixelHeight >= minimumPixelHeight)
            {
                _lru.Remove(entry.Node);
                _lru.AddFirst(entry.Node);
                image = entry.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    internal void Store(string stamp, BitmapSource? image)
        => StoreByKey(ThumbnailKey(stamp), image, 0, 0);

    private void StoreByKey(
        string key, BitmapSource? image, int requestedPixelWidth, int requestedPixelHeight)
    {
        if (!ShouldCache(image))
        {
            return;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var previous)
                && (previous.RequestedPixelWidth > requestedPixelWidth
                    || previous.RequestedPixelHeight > requestedPixelHeight)
                && previous.RequestedPixelWidth >= requestedPixelWidth
                && previous.RequestedPixelHeight >= requestedPixelHeight)
            {
                _lru.Remove(previous.Node);
                _lru.AddFirst(previous.Node);
                return;
            }

            if (_cache.Remove(key, out previous))
            {
                _lru.Remove(previous.Node);
            }

            var node = _lru.AddFirst(key);
            _cache[key] = new CacheEntry(
                image!, requestedPixelWidth, requestedPixelHeight, node);

            while (_cache.Count > _cacheCapacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _cache.Remove(oldest.Value);
            }
        }
    }

    private static string ThumbnailKey(string stamp) => "T:" + stamp;

    private static string ShellVisualKey(string stamp) => "V:" + stamp;

    internal static bool ShouldCache(BitmapSource? image) => image is not null;

    public Task<BitmapSource?> GetThumbnailAsync(string path, int pixelWidth, int pixelHeight)
    {
        string stamp;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return Task.FromResult<BitmapSource?>(null);
            }

            stamp = ComputeStamp(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        var key = ThumbnailKey(stamp);
        if (TryGetCached(key, pixelWidth, pixelHeight, out var cached))
        {
            return Task.FromResult(cached);
        }

        return FetchAndStoreAsync(
            path, key, pixelWidth, pixelHeight, thumbnailOnly: true, CancellationToken.None);
    }

    public Task<BitmapSource?> GetShellVisualAsync(
        string path, bool isDirectory, int pixelWidth, int pixelHeight,
        CancellationToken cancellationToken = default)
    {
        if (!TryComputeStamp(path, isDirectory, out var stamp))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        var key = ShellVisualKey(stamp);
        if (TryGetCached(key, pixelWidth, pixelHeight, out var cached))
        {
            return Task.FromResult(cached);
        }

        return FetchAndStoreAsync(
            path, key, pixelWidth, pixelHeight, thumbnailOnly: false, cancellationToken);
    }

    private async Task<BitmapSource?> FetchAndStoreAsync(
        string path,
        string key,
        int pixelWidth,
        int pixelHeight,
        bool thumbnailOnly,
        CancellationToken cancellationToken)
    {
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetCached(key, pixelWidth, pixelHeight, out var cached))
            {
                return cached;
            }

            var image = await Task.Run(
                () => _fetch(path, pixelWidth, pixelHeight, thumbnailOnly),
                CancellationToken.None).ConfigureAwait(false);
            StoreByKey(key, image, pixelWidth, pixelHeight);
            return image;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private static bool TryComputeStamp(string path, bool isDirectory, out string stamp)
    {
        try
        {
            if (isDirectory)
            {
                var directory = new DirectoryInfo(path);
                if (!directory.Exists)
                {
                    stamp = string.Empty;
                    return false;
                }

                stamp = ComputeStamp(path, 0, directory.LastWriteTimeUtc);
                return true;
            }

            var file = new FileInfo(path);
            if (!file.Exists)
            {
                stamp = string.Empty;
                return false;
            }

            stamp = ComputeStamp(path, file.Length, file.LastWriteTimeUtc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            stamp = string.Empty;
            return false;
        }
    }

    private static BitmapSource? Fetch(
        string path, int pixelWidth, int pixelHeight, bool thumbnailOnly)
    {
        var iid = typeof(IShellItem).GUID;
        var size = new SIZE { cx = pixelWidth, cy = pixelHeight };
        var flags = ShellItemNative.SIIGBF_BIGGERSIZEOK;
        if (thumbnailOnly)
        {
            flags |= ShellItemNative.SIIGBF_THUMBNAILONLY;
        }

        object? item = null;

        try
        {
            int hr;
            var hbitmap = IntPtr.Zero;

            using (FpuGuard.Enter("IShellItemImageFactory"))
            {
                hr = ShellItemNative.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item);

                if (hr != 0 || item is not IShellItemImageFactory factory)
                {
                    return null;
                }

                hr = factory.GetImage(size, flags, out hbitmap);
            }

            if (hr != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                ShellItemNative.DeleteObject(hbitmap);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }
}
