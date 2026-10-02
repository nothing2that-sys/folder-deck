using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

public partial class MacroDialog : Window
{
    private readonly bool _isEdit;

    public MacroDialog(MacroDraft draft, bool canFixPath, bool isEdit)
    {
        InitializeComponent();

        _isEdit = isEdit;

        Title = isEdit ? "규칙 고치기" : "규칙 만들기";
        AcceptButton.Content = "저장";

        AdvancedExpander.IsExpanded = isEdit;

        NameBox.Text = draft.Name;

        OpCopy.IsChecked = draft.Op == FileOperationKind.Copy;
        OpMove.IsChecked = draft.Op == FileOperationKind.Move;
        OpTrash.IsChecked = draft.Op == FileOperationKind.Trash;

        SourceFixed.IsEnabled = canFixPath;
        FixedPathText.Text = canFixPath ? draft.FixedPath : "(대상이 하나일 때만 고를 수 있다)";
        SourceFixed.IsChecked = canFixPath && draft.SourceKind == MacroSourceKind.FixedPath;
        SourceSelection.IsChecked = SourceFixed.IsChecked != true;
        RetargetFixedPathBox.IsChecked = draft.RetargetFixedPath;

        DestTray.IsChecked = draft.DestKind == MacroDestKind.Tray;
        DestFolders.IsChecked = draft.DestKind == MacroDestKind.FolderIds;
        DestVisible.IsChecked = draft.DestKind == MacroDestKind.AllVisible;
        RepinFolderIdsBox.IsChecked = draft.RepinFolderIds;

        ConflictOverwrite.IsChecked = draft.OnConflict == ConflictPolicy.Overwrite;
        ConflictSkip.IsChecked = draft.OnConflict == ConflictPolicy.Skip;
        ConflictRename.IsChecked = draft.OnConflict == ConflictPolicy.Rename;

        ConfirmBox.IsChecked = draft.Confirm;

        ApplyOpRules();
        ApplySourceRules();
        ApplyDestRules();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public MacroDraft? Result { get; private set; }

    private void ApplyOpRules()
    {
        var trash = OpTrash.IsChecked == true;

        DestLabel.IsEnabled = !trash;
        DestGroup.IsEnabled = !trash;
        ConflictGroup.IsEnabled = !trash;

        var forced = OpCopy.IsChecked != true;
        if (forced)
        {
            ConfirmBox.IsChecked = true;
        }

        ConfirmBox.IsEnabled = !forced;
        ConfirmNote.Visibility = forced ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySourceRules()
    {
        RetargetFixedPathBox.Visibility = _isEdit && SourceFixed.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyDestRules()
    {
        RepinFolderIdsBox.Visibility = _isEdit && DestFolders.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnOpChanged(object sender, RoutedEventArgs e)
    {

        if (IsInitialized)
        {
            ApplyOpRules();
        }
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            ApplySourceRules();
        }
    }

    private void OnDestChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            ApplyDestRules();
        }
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (name.Length == 0)
        {

            NameBox.Focus();
            return;
        }

        Result = new MacroDraft(
            name,
            OpTrash.IsChecked == true ? FileOperationKind.Trash
                : OpMove.IsChecked == true ? FileOperationKind.Move
                : FileOperationKind.Copy,
            SourceFixed.IsChecked == true ? MacroSourceKind.FixedPath : MacroSourceKind.Selection,
            FixedPathText.Text,
            DestVisible.IsChecked == true ? MacroDestKind.AllVisible
                : DestFolders.IsChecked == true ? MacroDestKind.FolderIds
                : MacroDestKind.Tray,
            ConflictSkip.IsChecked == true ? ConflictPolicy.Skip
                : ConflictRename.IsChecked == true ? ConflictPolicy.Rename
                : ConflictPolicy.Overwrite,
            ConfirmBox.IsChecked == true,
            RetargetFixedPath: RetargetFixedPathBox.IsChecked == true,
            RepinFolderIds: RepinFolderIdsBox.IsChecked == true);

        DialogResult = true;
    }
}

public sealed class MacroEditor : IMacroEditor
{
    public MacroDraft? Edit(MacroDraft draft, bool canFixPath, bool isEdit)
    {
        var dialog = new MacroDialog(draft, canFixPath, isEdit)
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
