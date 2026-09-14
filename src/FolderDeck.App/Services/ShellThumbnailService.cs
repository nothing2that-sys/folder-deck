using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FolderDeck.App.Interop;

namespace FolderDeck.App.Services;

/// <summary>
/// 파일 목록 미리보기 폭포(§3.2) 중 <b>② 단</b> — 셸에서 썸네일을 얻어 온다(Phase 17 · D86 · D95).
/// 취득 · 변환 · 캐시 · 가드를 한 파일에 둔다 — <c>HBITMAP</c> 의 수명이 넷을 관통한다
/// (<see cref="ShellIconService"/> 와 같은 이유).
/// </summary>
/// <remarks>
/// <b><see cref="IDisposable"/> 이 아니다</b> — <see cref="ShellIconService"/> 와 같다.
/// <para>
/// 캐시는 <b>64칸 LRU</b>다(Phase 21 · D126). 미리보기 한 칸뿐이던 D95 때는 한 칸이면
/// 충분했지만, 큰 아이콘 격자는 한 화면에서도 수십 경로를 비동기로 요청한다. 패널마다 하나라는
/// 수명(D95 §2 결정 5)은 그대로 유지해 다른 타일이 이 패널의 캐시를 밀어내지 않는다.
/// </para>
/// </remarks>
public sealed class ShellThumbnailService
{
    internal const int DefaultCacheCapacity = 64;

    // 세 패널이 동시에 큰 아이콘을 실현해도 Shell COM 작업은 둘만 실제로 실행한다. 대기
    // 요청은 thread pool을 차지하지 않고, 화면 밖으로 나가면 token으로 시작 전에 취소된다.
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

    /// <summary>
    /// 캐시 도장 — 경로(소문자) · 길이 · 수정 시각을 합친 것(D95 §5 R3). 셋 중 하나만 바뀌어도
    /// 다른 도장이 나와 캐시가 미스한다. 경로는 <c>ToLowerInvariant()</c>(<see
    /// cref="ShellIconService.ComputeCacheKey"/> 와 같은 결).
    /// </summary>
    internal static string ComputeStamp(string path, long length, DateTime modifiedUtc) =>
        $"{path.ToLowerInvariant()}|{length}|{modifiedUtc:O}";

    /// <summary>캐시에 있으면 그 자리에서 준다. 없으면 false — <b>셸을 부르지 않는다.</b></summary>
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

    /// <summary><see cref="ShouldCache"/> 가 참일 때만 LRU 캐시에 넣는다.</summary>
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

    /// <summary>
    /// 얻은 이미지를 캐시에 넣어야 하는가 — <paramref name="image"/> 이 <see langword="null"/>
    /// 이 아닐 때만 참이다. <c>null</c>(실패)을 캐시하면 그 도장이 세션 내내 빈 칸으로 굳는다
    /// (<see cref="ShellIconService.ShouldCache"/> 와 같은 이유).
    /// </summary>
    internal static bool ShouldCache(BitmapSource? image) => image is not null;

    /// <summary>
    /// 셸 썸네일을 비동기로 얻는다. <b>캐시 적중이면 스레드를 갈아타지 않고</b> 완료된
    /// <see cref="Task"/> 를 돌려준다(<see cref="ShellIconService.GetIconAsync"/> 와 같은 결).
    /// </summary>
    public Task<BitmapSource?> GetThumbnailAsync(string path, int pixelWidth, int pixelHeight)
    {
        string stamp;

        // 호출자가 async void 라 여기서 던지면 앱이 내려간다. ③ 단으로 떨어뜨리는 것이
        // 정상 경로다(파일이 사라졌거나 NAS 가 끊긴 경우를 포함).
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

    /// <summary>
    /// 큰 아이콘 격자용 Shell visual. Windows Shell이 thumbnail을 제공하면 thumbnail을,
    /// 아니면 해당 형식이나 폴더의 큰 아이콘을 돌려준다. 셸 호출은 항상 thread pool에서 돈다.
    /// </summary>
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

            // 같은 항목의 앞 요청이 gate를 기다리는 동안 cache를 채웠을 수 있다.
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

    /// <summary>
    /// 호출 자리는 이 하나뿐이다 — <b>되풀이하지 않는다</b>(D95 §5 R3 — <see
    /// cref="ShellIconService"/> 의 두 번 되풀이는 D73 이 실측한 <c>SHGetFileInfo</c> 고유
    /// 현상이라 여기서는 흉내 내지 않는다). <c>SHCreateItemFromParsingName</c>·<c>GetImage</c>
    /// 둘 다 셸 호출이라 <see cref="FpuGuard.Enter"/> 한 스코프로 같이 감싼다.
    /// <c>CreateBitmapSourceFromHBitmap</c> 변환은 가드 밖이다 — <see
    /// cref="ShellIconService.Fetch"/> 가 그렇게 하고 있다.
    /// </summary>
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
