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
/// 캐시는 <b>한 칸</b>이다(D95 §2 결정 4) — 미리보기 칸에는 늘 파일 하나만 뜨므로, 스플리터를
/// 끄는 동안 같은 파일을 다시 묻는 것만 막으면 된다. <see cref="ShellIconService"/> 처럼 여럿을
/// 들고 있을 이유가 없다. 이 서비스는 <b>패널마다 하나</b>다(D95 §2 결정 5) — 앱 전체가 한 칸을
/// 나눠 쓰면 타일 둘이 서로 밀어낸다.
/// </para>
/// </remarks>
public sealed class ShellThumbnailService
{
    private readonly object _gate = new();
    private string? _cachedStamp;
    private BitmapSource? _cachedImage;

    /// <summary>
    /// 캐시 도장 — 경로(소문자) · 길이 · 수정 시각을 합친 것(D95 §5 R3). 셋 중 하나만 바뀌어도
    /// 다른 도장이 나와 캐시가 미스한다. 경로는 <c>ToLowerInvariant()</c>(<see
    /// cref="ShellIconService.ComputeCacheKey"/> 와 같은 결).
    /// </summary>
    internal static string ComputeStamp(string path, long length, DateTime modifiedUtc) =>
        $"{path.ToLowerInvariant()}|{length}|{modifiedUtc:O}";

    /// <summary>캐시에 있으면 그 자리에서 준다. 없으면 false — <b>셸을 부르지 않는다.</b></summary>
    internal bool TryGetCached(string stamp, out BitmapSource? image)
    {
        lock (_gate)
        {
            if (_cachedStamp == stamp)
            {
                image = _cachedImage;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <summary><see cref="ShouldCache"/> 가 참일 때만 한 칸을 갈아 끼운다.</summary>
    internal void Store(string stamp, BitmapSource? image)
    {
        if (!ShouldCache(image))
        {
            return;
        }

        lock (_gate)
        {
            _cachedStamp = stamp;
            _cachedImage = image;
        }
    }

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

        if (TryGetCached(stamp, out var cached))
        {
            return Task.FromResult(cached);
        }

        return Task.Run(() =>
        {
            var image = Fetch(path, pixelWidth, pixelHeight);
            Store(stamp, image);
            return image;
        });
    }

    /// <summary>
    /// 호출 자리는 이 하나뿐이다 — <b>되풀이하지 않는다</b>(D95 §5 R3 — <see
    /// cref="ShellIconService"/> 의 두 번 되풀이는 D73 이 실측한 <c>SHGetFileInfo</c> 고유
    /// 현상이라 여기서는 흉내 내지 않는다). <c>SHCreateItemFromParsingName</c>·<c>GetImage</c>
    /// 둘 다 셸 호출이라 <see cref="FpuGuard.Enter"/> 한 스코프로 같이 감싼다.
    /// <c>CreateBitmapSourceFromHBitmap</c> 변환은 가드 밖이다 — <see
    /// cref="ShellIconService.Fetch"/> 가 그렇게 하고 있다.
    /// </summary>
    private static BitmapSource? Fetch(string path, int pixelWidth, int pixelHeight)
    {
        var iid = typeof(IShellItem).GUID;
        var size = new SIZE { cx = pixelWidth, cy = pixelHeight };
        const int flags = ShellItemNative.SIIGBF_BIGGERSIZEOK | ShellItemNative.SIIGBF_THUMBNAILONLY;

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
