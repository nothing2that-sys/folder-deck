using System.Windows;
using FolderDeck.App.Services;

namespace FolderDeck.App.Views;

public partial class FolderEditDialog : Window
{
    public FolderEditDialog(FolderEditDraft draft, string path)
    {
        InitializeComponent();

        PathText.Text = path;
        NameBox.Text = draft.DisplayName ?? string.Empty;
        DescriptionBox.Text = draft.Description ?? string.Empty;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public FolderEditDraft? Result { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {

        Result = new FolderEditDraft(Blank(NameBox.Text), Blank(DescriptionBox.Text));
        DialogResult = true;
    }

    private static string? Blank(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public sealed class FolderEditor : IFolderEditor
{
    public FolderEditDraft? Edit(FolderEditDraft draft, string path)
    {
        var dialog = new FolderEditDialog(draft, path)
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
