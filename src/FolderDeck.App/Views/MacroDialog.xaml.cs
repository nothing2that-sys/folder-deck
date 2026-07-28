using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

/// <summary>매크로 규칙 편집 창 (§4.7 · Phase 3 Stage 5).</summary>
public partial class MacroDialog : Window
{
    public MacroDialog(MacroDraft draft, bool canFixPath)
    {
        InitializeComponent();

        NameBox.Text = draft.Name;

        OpCopy.IsChecked = draft.Op == FileOperationKind.Copy;
        OpMove.IsChecked = draft.Op == FileOperationKind.Move;
        OpTrash.IsChecked = draft.Op == FileOperationKind.Trash;

        // 대상이 여럿이면 경로 하나로 굳힐 수 없다 — §4.7 의 source.path 는 경로 하나다.
        SourceFixed.IsEnabled = canFixPath;
        FixedPathText.Text = canFixPath ? draft.FixedPath : "(대상이 하나일 때만 고를 수 있다)";
        SourceFixed.IsChecked = canFixPath && draft.SourceKind == MacroSourceKind.FixedPath;
        SourceSelection.IsChecked = SourceFixed.IsChecked != true;

        DestTray.IsChecked = draft.DestKind == MacroDestKind.Tray;
        DestFolders.IsChecked = draft.DestKind == MacroDestKind.FolderIds;
        DestVisible.IsChecked = draft.DestKind == MacroDestKind.AllVisible;

        ConflictOverwrite.IsChecked = draft.OnConflict == ConflictPolicy.Overwrite;
        ConflictSkip.IsChecked = draft.OnConflict == ConflictPolicy.Skip;
        ConflictRename.IsChecked = draft.OnConflict == ConflictPolicy.Rename;

        ConfirmBox.IsChecked = draft.Confirm;

        ApplyOpRules();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public MacroDraft? Result { get; private set; }

    /// <summary>
    /// 동작에 따라 못 고르는 칸을 잠근다 — 휴지통은 목적지·충돌이 없고,
    /// 이동·휴지통은 확인을 끄지 못한다 (§4.7).
    /// </summary>
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

    private void OnOpChanged(object sender, RoutedEventArgs e)
    {
        // 생성자에서 IsChecked 를 세울 때도 불린다 — 아직 만들어지지 않은 칸이 있을 수 있다.
        if (IsInitialized)
        {
            ApplyOpRules();
        }
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (name.Length == 0)
        {
            // 이름 없는 카드는 목록에서 서로 구별되지 않는다.
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
            ConfirmBox.IsChecked == true);

        DialogResult = true;
    }
}

/// <inheritdoc/>
public sealed class MacroEditor : IMacroEditor
{
    public MacroDraft? Edit(MacroDraft draft, bool canFixPath)
    {
        var dialog = new MacroDialog(draft, canFixPath)
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
