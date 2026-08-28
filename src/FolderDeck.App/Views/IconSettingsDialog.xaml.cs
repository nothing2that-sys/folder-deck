using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

/// <summary>설정 창 — 지금은 「아이콘」 탭 하나(D59).</summary>
public partial class IconSettingsDialog : Window
{
    public IconSettingsDialog(IReadOnlyDictionary<string, string> current)
    {
        InitializeComponent();
        DataContext = new IconSettingsViewModel(current);
        EverythingTabContent.DataContext = new EverythingSettingsViewModel(EverythingProbe.Detect());
    }

    public IReadOnlyDictionary<string, string>? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = ((IconSettingsViewModel)DataContext!).ToDictionary();
        DialogResult = true;
    }
}

/// <inheritdoc/>
public sealed class IconSettingsEditor : IIconSettingsEditor
{
    public IReadOnlyDictionary<string, string>? Edit(IReadOnlyDictionary<string, string> current)
    {
        var dialog = new IconSettingsDialog(current)
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
