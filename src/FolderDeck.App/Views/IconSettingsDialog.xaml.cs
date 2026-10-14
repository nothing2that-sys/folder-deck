using System.Windows;
using System.Windows.Input;
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
        ScratchTabContent.DataContext = new ScratchSettingsViewModel(
            request.ScratchLayout, request.ScratchHotkey, request.ScratchShortcutInstalled);
    }

    public SettingsEditResult? Result { get; private set; }

    /// <summary>
    /// 단축키 칸 — 눌린 조합을 그대로 잡는다(S4). 글자를 입력받는 칸이 아니라 모든 키를 여기서 먹는다.
    /// Tab 만 흘려보내 키보드로 칸을 빠져나갈 수 있게 둔다.
    /// </summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
        {
            return;
        }

        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        ((ScratchSettingsViewModel)ScratchTabContent.DataContext!).Capture(
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Shift),
            KeyName(key));
    }

    /// <summary>WPF 키 → 단축키 규칙의 키 이름. 규칙 밖의 키는 그 이름 그대로 넘겨 거절 이유를 받게 한다.</summary>
    internal static string KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        _ => key.ToString(),
    };

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var iconVm = (IconSettingsViewModel)DataContext!;
        var everythingVm = (EverythingSettingsViewModel)EverythingTabContent.DataContext!;
        var scratchVm = (ScratchSettingsViewModel)ScratchTabContent.DataContext!;

        Result = new SettingsEditResult(
            iconVm.ToDictionary(), iconVm.ShowShellIcons, everythingVm.EverythingMaxResults,
            scratchVm.SelectedLayout.Preset, scratchVm.HotkeyText);
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
