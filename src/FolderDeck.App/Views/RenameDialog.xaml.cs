using System.Windows;

namespace FolderDeck.App.Views;


public partial class RenameDialog : Window
{
    public RenameDialog(string title, string message, string current, string acceptButtonText)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        NameBox.Text = current;
        AcceptButton.Content = acceptButtonText;

        Loaded += (_, _) =>
        {
            NameBox.Focus();




            var dot = current.LastIndexOf('.');
            if (dot > 0)
            {
                NameBox.Select(0, dot);
            }
            else
            {
                NameBox.SelectAll();
            }
        };
    }


    public string? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = NameBox.Text;
        DialogResult = true;
    }
}
