using System.Windows;
using System.Windows.Input;

namespace FolderDeck.App.Views;


public partial class PickOneDialog : Window
{
    public PickOneDialog(string title, string message, IReadOnlyList<string> options)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        OptionList.ItemsSource = options;






        if (options.Count > 0)
        {
            OptionList.SelectedIndex = 0;
        }

        Loaded += (_, _) => OptionList.Focus();
    }


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
