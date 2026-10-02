using System.Collections.Generic;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.App.Services;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;






public sealed partial class FileItemViewModel(
    FolderItem item,
    string relativeFolder = "",
    IReadOnlyDictionary<string, string>? extensionGlyphs = null,
    ShellIconService? shellIconService = null) : ObservableObject
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    private static readonly IReadOnlyDictionary<string, string> EmptyGlyphs =
        new Dictionary<string, string>();

    private readonly IReadOnlyDictionary<string, string> _extensionGlyphs = extensionGlyphs ?? EmptyGlyphs;

    private readonly ShellIconService? _shellIconService = shellIconService;

    private bool _shellIconRequested;

    private CancellationTokenSource? _shellIconCancellation;

    public FolderItem Item { get; } = item;




    public string RelativeFolder { get; } = relativeFolder;

    public bool HasRelativeFolder => RelativeFolder.Length > 0;


    public string RelativeFolderPrefix =>
        HasRelativeFolder ? RelativeFolder + System.IO.Path.DirectorySeparatorChar : string.Empty;

    public string Name => Item.Name;

    public string FullPath => Item.FullPath;

    public bool IsDirectory => Item.IsDirectory;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellIcon))]
    private BitmapSource? icon;







    public BitmapSource? ShellIcon
    {
        get
        {
            EnsureShellIconRequested();
            return Icon;
        }
    }

    private void EnsureShellIconRequested()
    {
        if (_shellIconRequested || _shellIconService is null)
        {
            return;
        }

        _shellIconRequested = true;
        var service = _shellIconService;

        if (TryGetCustomGlyph(out _))
        {
            return;
        }

        if (service.TryGetCached(FullPath, IsDirectory, out var cached))
        {
            Icon = cached;
            return;
        }

        FetchShellIconAsync(service);
    }

    private async void FetchShellIconAsync(ShellIconService service)
    {
        var cancellation = new CancellationTokenSource();
        _shellIconCancellation = cancellation;

        try
        {
            var icon = await service.GetIconAsync(FullPath, IsDirectory, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                Icon = icon;
            }
        }
        catch
        {

        }
        finally
        {
            if (ReferenceEquals(_shellIconCancellation, cancellation))
            {
                _shellIconCancellation = null;
            }

            cancellation.Dispose();
        }
    }








    internal void CancelPendingShellIconFetch()
    {
        if (_shellIconCancellation is null)
        {
            return;
        }

        _shellIconCancellation.Cancel();
        _shellIconRequested = false;
    }






    private bool TryGetCustomGlyph(out string glyph)
    {
        glyph = string.Empty;

        if (Item.IsDirectory)
        {
            return false;
        }

        var extension = AppSettings.NormalizeExtension(System.IO.Path.GetExtension(Item.Name));
        if (extension.Length > 0 && _extensionGlyphs.TryGetValue(extension, out var mapped) && mapped.Length > 0)
        {
            glyph = mapped;
            return true;
        }

        return false;
    }




























    public string Glyph
    {
        get
        {
            if (Item.IsDirectory)
            {
                return "📁";
            }

            return TryGetCustomGlyph(out var glyph) ? glyph : "📄";
        }
    }







    [ObservableProperty]
    private CompareStatus compareStatus = CompareStatus.Unset;


    public string SizeText => Item.IsDirectory ? string.Empty : Humanize(Item.Size);

    public string ModifiedText => Item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string Humanize(long? bytes)
    {
        if (bytes is null)
        {
            return string.Empty;
        }

        double size = bytes.Value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes.Value:N0} B" : $"{size:0.#} {Units[unit]}";
    }
}
