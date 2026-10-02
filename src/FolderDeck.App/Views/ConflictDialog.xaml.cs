using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

public partial class ConflictDialog : Window
{
    public ConflictDialog(string headline, string message, ConflictPolicy suggested)
    {
        InitializeComponent();

        HeadlineText.Text = headline;
        MessageText.Text = message;

        var initial = suggested switch
        {
            ConflictPolicy.Skip => SkipButton,
            ConflictPolicy.Rename => RenameButton,
            _ => OverwriteButton,
        };

        initial.IsDefault = true;
        Loaded += (_, _) => initial.Focus();
    }

    public ConflictDecision? Decision { get; private set; }

    private void OnOverwrite(object sender, RoutedEventArgs e) => Close(ConflictPolicy.Overwrite);

    private void OnSkip(object sender, RoutedEventArgs e) => Close(ConflictPolicy.Skip);

    private void OnRename(object sender, RoutedEventArgs e) => Close(ConflictPolicy.Rename);

    private void Close(ConflictPolicy policy)
    {
        Decision = new ConflictDecision(policy, RememberBox.IsChecked == true);
        DialogResult = true;
    }
}
