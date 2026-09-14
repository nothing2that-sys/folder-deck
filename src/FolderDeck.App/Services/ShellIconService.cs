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
/// 이 회차는 취득 계층만 만든다 — <b>배선은 D68-c 가 끝냈다</b>(<c>FileItemViewModel</c> ·
/// <c>MainViewModel</c>).
/// </remarks>
public sealed class ShellIconService
{
    private const string PathKeyPrefix = "P:";
    private const string ExtensionKeyPrefix = "E:";

    private static readonly HashSet<string> PathKeyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico" };

    // SHGetFileInfo는 서비스 인스턴스가 달라도 같은 process-global Shell 경로를 탄다. 전체
    // 시험과 실제 목록에서 동시 호출 때 산발 null이 재현되므로 호출 자체를 한 번에 하나로
    // 제한한다. wait는 비동기이고 실제 native 호출만 worker thread에서 돌아 UI를 막지 않는다.
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

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
            return PathKeyPrefix + path.ToLowerInvariant();
        }

        var extension = AppSettings.NormalizeExtension(Path.GetExtension(path));
        if (extension.Length == 0 || PathKeyExtensions.Contains(extension))
        {
            return PathKeyPrefix + path.ToLowerInvariant();
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
        return FetchAndCacheAsync(path, isDirectory, key, useFileAttributes);
    }

    private async Task<BitmapSource?> FetchAndCacheAsync(
        string path, bool isDirectory, string key, bool useFileAttributes)
    {
        await FetchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // 같은 key의 앞선 요청이 gate를 기다리는 동안 성공했으면 native 호출을 생략한다.
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var icon = await Task.Run(
                () => Fetch(path, useFileAttributes, isDirectory)).ConfigureAwait(false);

            // 실패(null)는 캐시하지 않는다(D74) — 넣으면 확장자 하나가 한 번 헛나갔을 때
            // 그 확장자가 세션 내내 📄 로 굳는다(D73 이 잰 산발 실패의 원인 하나).
            if (ShouldCache(icon))
            {
                _cache[key] = icon;
            }

            return icon;
        }
        finally
        {
            FetchGate.Release();
        }
    }

    /// <summary>모든 캐시를 비운다(언제 부를지는 D68-c 가 정한다).</summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// 얻은 이미지를 캐시에 넣어야 하는가 — <paramref name="icon"/> 이 <see langword="null"/>
    /// 이 아닐 때만 참이다(D74). <c>null</c>(실패)을 캐시하면 그 키가 다시 물어도 계속
    /// <c>null</c> 을 돌려주므로 넣지 않는다.
    /// </summary>
    internal static bool ShouldCache(BitmapSource? icon) => icon is not null;

    /// <summary>
    /// <see cref="ShellIconNative.SHGetFileInfo"/> 를 다시 불러야 하는가 — <paramref name="result"/>
    /// 가 usable icon을 주지 않았을 때 참이다. D73에서는 성공 반환과 빈 handle만 측정됐지만,
    /// 이후 전체 시험과 실제 목록에서 최종 <c>null</c>이 다시 관측됐다. 반환값까지 0인 빈 결과도
    /// 같은 제한된 되풀이 안에서 한 번 더 부르되, handle이 있으면 반환값과 관계없이 끝낸다.
    /// </summary>
    internal static bool ShouldRetryFetch(IntPtr result, IntPtr hIcon) =>
        hIcon == IntPtr.Zero;

    /// <summary>
    /// 실제 폴더 경로에서 고유 Shell 아이콘을 끝내 얻지 못한 경우에만 일반 directory Shell
    /// 아이콘을 요청한다. 파일이나 이미 attributes 기반인 요청에는 fallback을 겹치지 않는다.
    /// </summary>
    internal static bool ShouldUseDirectoryFallback(
        bool isDirectory, bool useFileAttributes, bool iconWasReturned) =>
        isDirectory && !useFileAttributes && !iconWasReturned;

    /// <summary>
    /// 호출 자리는 이 하나뿐이다 — <b>되풀이로 최대 두 번</b> 돈다(D74). 확장자 키
    /// (<paramref name="useFileAttributes"/>)면 <see cref="ShellIconNative.SHGFI_USEFILEATTRIBUTES"/>
    /// 로 디스크를 건드리지 않는다 — NAS 가 끊겨 있거나 경로가 없어도 아이콘이 나온다. 경로
    /// 키면 그 파일 고유의 아이콘을 얻어야 하므로 이 플래그를 뺀다.
    /// </summary>
    /// <remarks>
    /// <b>왜 다시 부르는가</b> — D73 이 스레드풀 스레드의 첫 호출에서만, API 가 성공을
    /// 알리면서도 <c>hIcon</c> 을 안 주는 것을 관측했다(80회 중 7회, 전부 그 스레드의 첫
    /// 호출). 같은 스레드·같은 경로로 곧장 다시 부르면 7/7 이 성공했다 — 그래서 한 번만
    /// 되풀이한다(§2 "재시도는 한 번만").
    /// </remarks>
    private static BitmapSource? Fetch(string path, bool useFileAttributes, bool isDirectory)
    {
        var icon = FetchCore(path, useFileAttributes, isDirectory);
        if (ShouldUseDirectoryFallback(isDirectory, useFileAttributes, icon is not null))
        {
            icon = FetchCore(path, useFileAttributes: true, isDirectory: true);
        }

        return icon;
    }

    private static BitmapSource? FetchCore(string path, bool useFileAttributes, bool isDirectory)
    {
        var flags = ShellIconNative.SHGFI_ICON | ShellIconNative.SHGFI_SMALLICON;
        uint attributes = 0;

        if (useFileAttributes)
        {
            flags |= ShellIconNative.SHGFI_USEFILEATTRIBUTES;
            attributes = isDirectory
                ? ShellIconNative.FILE_ATTRIBUTE_DIRECTORY
                : ShellIconNative.FILE_ATTRIBUTE_NORMAL;
        }

        var info = new ShellIconNative.SHFILEINFO();
        var result = IntPtr.Zero;

        using (FpuGuard.Enter("SHGetFileInfo"))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                result = ShellIconNative.SHGetFileInfo(
                    path, attributes, ref info, (uint)Marshal.SizeOf<ShellIconNative.SHFILEINFO>(), flags);

                if (!ShouldRetryFetch(result, info.hIcon))
                {
                    break;
                }
            }
        }

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            if (info.hIcon != IntPtr.Zero)
            {
                ShellIconNative.DestroyIcon(info.hIcon);
            }

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
