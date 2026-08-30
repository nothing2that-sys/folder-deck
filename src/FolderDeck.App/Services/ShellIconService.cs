using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FolderDeck.App.Interop;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>
/// 파일 목록 아이콘 3단(§3.2) 중 <b>② 단</b> — 셸에서 아이콘을 얻어 온다(Phase 11 · D68-b).
/// 취득 · 변환 · 캐시 · 가드를 한 파일에 둔다 — <c>HICON</c> 의 수명이 넷을 관통하고,
/// 갈라 두면 해제가 <c>finally</c> 밖으로 나가는 자리가 생긴다(D52 · D66 Q5).
/// </summary>
/// <remarks>
/// 이 회차는 취득 계층만 만든다 — <b>이 클래스를 부르는 자리는 아직 코드 어디에도 없다.</b>
/// 배선(뷰모델·XAML·DI)은 D68-c 의 몫이다(§1).
/// </remarks>
public sealed class ShellIconService
{
    private const string PathKeyPrefix = "P:";
    private const string ExtensionKeyPrefix = "E:";

    private static readonly HashSet<string> PathKeyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico" };

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new();

    /// <summary>
    /// 캐시 키를 뽑는다 — 폴더 · <c>.exe</c> · <c>.lnk</c> · <c>.ico</c> 는 전체 경로,
    /// 나머지는 정규화한 확장자(<see cref="AppSettings.NormalizeExtension"/>). <c>P:</c>/
    /// <c>E:</c> 접두사로 두 키 공간을 갈라, 경로가 우연히 확장자 문자열과 같아지는 경우와도
    /// 절대 부딪히지 않게 한다.
    /// </summary>
    internal static string ComputeCacheKey(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return PathKeyPrefix + path;
        }

        var extension = AppSettings.NormalizeExtension(Path.GetExtension(path));
        if (extension.Length == 0 || PathKeyExtensions.Contains(extension))
        {
            return PathKeyPrefix + path;
        }

        return ExtensionKeyPrefix + extension;
    }

    /// <summary>
    /// 캐시에 있으면 그 자리에서 준다. 없으면 false — <b>셸을 부르지 않는다.</b> 스크롤로
    /// 되돌아온 줄이 깜빡이지 않게 하는 자리다.
    /// </summary>
    public bool TryGetCached(string path, bool isDirectory, out BitmapSource? icon)
    {
        var key = ComputeCacheKey(path, isDirectory);
        return _cache.TryGetValue(key, out icon);
    }

    /// <summary>
    /// 셸 아이콘을 비동기로 얻는다. <b>캐시 적중이면 스레드를 갈아타지 않고</b> 완료된
    /// <see cref="Task"/> 를 돌려준다 — 보이는 줄마다 스레드를 한 번씩 태우면 안 된다.
    /// </summary>
    public Task<BitmapSource?> GetIconAsync(string path, bool isDirectory)
    {
        var key = ComputeCacheKey(path, isDirectory);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached);
        }

        var useFileAttributes = key.StartsWith(ExtensionKeyPrefix, StringComparison.Ordinal);
        return Task.Run(() =>
        {
            var icon = Fetch(path, useFileAttributes);

            // 실패(null)도 캐시한다 — 안 그러면 못 얻는 항목이 스크롤할 때마다 다시 호출된다.
            _cache[key] = icon;
            return icon;
        });
    }

    /// <summary>모든 캐시를 비운다(언제 부를지는 D68-c 가 정한다).</summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// 호출 자리는 이 하나뿐이다. 확장자 키(<paramref name="useFileAttributes"/>)면
    /// <see cref="ShellIconNative.SHGFI_USEFILEATTRIBUTES"/> 로 디스크를 건드리지 않는다 —
    /// NAS 가 끊겨 있거나 경로가 없어도 아이콘이 나온다. 경로 키면 그 파일 고유의 아이콘을
    /// 얻어야 하므로 이 플래그를 뺀다.
    /// </summary>
    private static BitmapSource? Fetch(string path, bool useFileAttributes)
    {
        var flags = ShellIconNative.SHGFI_ICON | ShellIconNative.SHGFI_SMALLICON;
        uint attributes = 0;

        if (useFileAttributes)
        {
            flags |= ShellIconNative.SHGFI_USEFILEATTRIBUTES;
            attributes = ShellIconNative.FILE_ATTRIBUTE_NORMAL;
        }

        var info = new ShellIconNative.SHFILEINFO();
        IntPtr result;

        using (FpuGuard.Enter("SHGetFileInfo"))
        {
            result = ShellIconNative.SHGetFileInfo(
                path, attributes, ref info, (uint)Marshal.SizeOf<ShellIconNative.SHFILEINFO>(), flags);
        }

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            ShellIconNative.DestroyIcon(info.hIcon);
        }
    }
}
