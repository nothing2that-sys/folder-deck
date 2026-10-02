using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FolderDeck.App.Interop;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

public sealed class ShellIconService
{
    private const string PathKeyPrefix = "P:";
    private const string ExtensionKeyPrefix = "E:";

    private static readonly HashSet<string> PathKeyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico" };

    private static readonly SemaphoreSlim SharedFetchGate = new(1, 1);

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new();
    private readonly SemaphoreSlim _fetchGate;
    private readonly Func<string, bool, bool, BitmapSource?> _fetch;

    public ShellIconService() : this(SharedFetchGate, Fetch)
    {
    }

    internal ShellIconService(SemaphoreSlim fetchGate, Func<string, bool, bool, BitmapSource?> fetch)
    {
        ArgumentNullException.ThrowIfNull(fetchGate);
        ArgumentNullException.ThrowIfNull(fetch);
        _fetchGate = fetchGate;
        _fetch = fetch;
    }

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

    public bool TryGetCached(string path, bool isDirectory, out BitmapSource? icon)
    {
        var key = ComputeCacheKey(path, isDirectory);
        return _cache.TryGetValue(key, out icon);
    }

    public Task<BitmapSource?> GetIconAsync(
        string path, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var key = ComputeCacheKey(path, isDirectory);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached);
        }

        var useFileAttributes = key.StartsWith(ExtensionKeyPrefix, StringComparison.Ordinal);
        return FetchAndCacheAsync(path, isDirectory, key, useFileAttributes, cancellationToken);
    }

    private async Task<BitmapSource?> FetchAndCacheAsync(
        string path, bool isDirectory, string key, bool useFileAttributes,
        CancellationToken cancellationToken)
    {
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {

            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var icon = await Task.Run(
                () => _fetch(path, useFileAttributes, isDirectory)).ConfigureAwait(false);

            if (ShouldCache(icon))
            {
                _cache[key] = icon;
            }

            return icon;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    public void Clear() => _cache.Clear();

    internal static bool ShouldCache(BitmapSource? icon) => icon is not null;

    internal static bool ShouldRetryFetch(IntPtr result, IntPtr hIcon) =>
        hIcon == IntPtr.Zero;

    internal static bool ShouldUseDirectoryFallback(
        bool isDirectory, bool useFileAttributes, bool iconWasReturned) =>
        isDirectory && !useFileAttributes && !iconWasReturned;

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
