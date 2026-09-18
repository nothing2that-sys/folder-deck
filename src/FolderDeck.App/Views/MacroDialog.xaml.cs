using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

/// <summary>매크로 규칙 편집 창 (§4.7 · Phase 3 Stage 5).</summary>
public partial class MacroDialog : Window
{
    private readonly bool _isEdit;

    public MacroDialog(MacroDraft draft, bool canFixPath, bool isEdit)
    {
        InitializeComponent();

        _isEdit = isEdit;

        Title = isEdit ? "규칙 고치기" : "규칙 만들기";
        AcceptButton.Content = "저장";

        // 고치기는 지금 값이 뭔지부터 보여야 한다 — 만들기는 대개 이름만 쓰면 되니 접어 둔다.
        AdvancedExpander.IsExpanded = isEdit;

        NameBox.Text = draft.Name;

        OpCopy.IsChecked = draft.Op == FileOperationKind.Copy;
        OpMove.IsChecked = draft.Op == FileOperationKind.Move;
        OpTrash.IsChecked = draft.Op == FileOperationKind.Trash;

        // 대상이 여럿이면 경로 하나로 굳힐 수 없다 — §4.7 의 source.path 는 경로 하나다.
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

    /// <summary>
    /// "지금 고른 파일로 다시 고정"은 고치기 중이면서 이미 고정 경로를 쓸 때만 뜻이 있다
    /// (코덱스 적대적 검증 발견 — 예전에는 고정 경로를 다시 지정할 방법이 아예 없었다).
    /// </summary>
    private void ApplySourceRules()
    {
        RetargetFixedPathBox.Visibility = _isEdit && SourceFixed.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// "지금 체크된 폴더로 다시 고정"도 같은 이유로 고치기 중일 때만 보인다
    /// (코덱스 적대적 검증 발견 — 예전에는 굳힌 폴더 목록을 갱신할 방법이 없었다).
    /// </summary>
    private void ApplyDestRules()
    {
        RepinFolderIdsBox.Visibility = _isEdit && DestFolders.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnOpChanged(object sender, RoutedEventArgs e)
    {
        // 생성자에서 IsChecked 를 세울 때도 불린다 — 아직 만들어지지 않은 칸이 있을 수 있다.
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
            ConfirmBox.IsChecked == true,
            RetargetFixedPath: RetargetFixedPathBox.IsChecked == true,
            RepinFolderIds: RepinFolderIdsBox.IsChecked == true);

        DialogResult = true;
    }
}

/// <inheritdoc/>
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
