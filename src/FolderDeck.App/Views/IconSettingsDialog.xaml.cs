using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

/// <summary>설정 창 — 「아이콘」·「검색」 두 탭(D59 · D65 · D69-d).</summary>
public partial class IconSettingsDialog : Window
{
    public IconSettingsDialog(SettingsEditRequest request)
    {
        InitializeComponent();
        DataContext = new IconSettingsViewModel(request.ExtensionGlyphs, request.ShowShellIcons);
        EverythingTabContent.DataContext =
            new EverythingSettingsViewModel(request.EverythingStatus, request.EverythingMaxResults);
    }

    public SettingsEditResult? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var iconVm = (IconSettingsViewModel)DataContext!;
        var everythingVm = (EverythingSettingsViewModel)EverythingTabContent.DataContext!;

        Result = new SettingsEditResult(iconVm.ToDictionary(), iconVm.ShowShellIcons, everythingVm.EverythingMaxResults);
        DialogResult = true;
    }
}

/// <inheritdoc/>
public sealed class IconSettingsEditor : ISettingsEditor
{
    public SettingsEditResult? Edit(SettingsEditRequest request)
    {
        var dialog = new IconSettingsDialog(request)
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
