using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Models;
using FolderDeck.Core.Renaming;

namespace FolderDeck.App.Services;








public sealed record ConflictDecision(ConflictPolicy Policy, bool Remember);


public interface IUserPrompt
{
    bool Confirm(string title, string message);




    void Report(string title, string message);








    ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested);


















    string? AskName(string title, string message, string current, string acceptButtonText);











    int? PickOne(string title, string message, IReadOnlyList<string> options);









    IReadOnlyList<RenamePreviewRow>? PlanBatchRename(
        IReadOnlyList<string> originalNames, IReadOnlyCollection<string> otherExistingNames);
}


public sealed class UserPrompt : IUserPrompt
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    public void Report(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);






    public ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested)
    {
        var dialog = new ConflictDialog(title, message, suggested)
        {

            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Decision : null;
    }


    public string? AskName(string title, string message, string current, string acceptButtonText)
    {
        var dialog = new RenameDialog(title, message, current, acceptButtonText)
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }


    public int? PickOne(string title, string message, IReadOnlyList<string> options)
    {
        var dialog = new PickOneDialog(title, message, options)
        {
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.PickedIndex : null;
    }


    public IReadOnlyList<RenamePreviewRow>? PlanBatchRename(
        IReadOnlyList<string> originalNames, IReadOnlyCollection<string> otherExistingNames)
    {
        var dialog = new BatchRenameDialog(originalNames, otherExistingNames)
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
