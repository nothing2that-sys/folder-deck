using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;


public sealed partial class IconMappingRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string extension = string.Empty;

    [ObservableProperty]
    private string glyph = string.Empty;
}






public sealed partial class IconSettingsViewModel : ObservableObject
{
    public IconSettingsViewModel(IReadOnlyDictionary<string, string> current, bool showShellIcons)
    {
        ShowShellIcons = showShellIcons;

        foreach (var pair in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Rows.Add(new IconMappingRowViewModel { Extension = pair.Key, Glyph = pair.Value });
        }
    }


    [ObservableProperty]
    private bool showShellIcons;

    public ObservableCollection<IconMappingRowViewModel> Rows { get; } = [];


    [RelayCommand]
    public void AddRow() => Rows.Add(new IconMappingRowViewModel());


    [RelayCommand]
    public void RemoveRow(IconMappingRowViewModel? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }







    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>();

        foreach (var row in Rows)
        {
            var key = AppSettings.NormalizeExtension(row.Extension);
            var glyph = row.Glyph.Trim();
            if (key.Length == 0 || glyph.Length == 0)
            {
                continue;
            }

            result[key] = glyph;
        }

        return result;
    }
}
