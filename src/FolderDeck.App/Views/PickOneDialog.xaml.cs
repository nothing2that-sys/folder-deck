using System.Windows;
using System.Windows.Input;

namespace FolderDeck.App.Views;

/// <summary>여럿 중 하나를 글자 목록으로 고르는 범용 창(2026-09-18).</summary>
public partial class PickOneDialog : Window
{
    public PickOneDialog(string title, string message, IReadOnlyList<string> options)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        OptionList.ItemsSource = options;

        Loaded += (_, _) => OptionList.Focus();
    }

    /// <summary>고른 자리(0부터). 취소면 <c>null</c>.</summary>
    public int? PickedIndex { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var index = OptionList.SelectedIndex;
        PickedIndex = index >= 0 ? index : null;
        DialogResult = PickedIndex is not null;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OptionList.SelectedIndex >= 0)
        {
            PickedIndex = OptionList.SelectedIndex;
            DialogResult = true;
        }
    }
}
