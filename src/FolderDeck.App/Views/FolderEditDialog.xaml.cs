using System.Windows;
using FolderDeck.App.Services;

namespace FolderDeck.App.Views;

/// <summary>폴더 등록 수정 창 (§4.11-3).</summary>
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
        // 빈 칸은 null 로 넘긴다 — 빈 문자열을 넣으면 파일에 아무 뜻도 없는 슬롯이 남고,
        // 표시 이름은 "잎 이름으로 되돌린다"와 구별되지 않는다.
        Result = new FolderEditDraft(Blank(NameBox.Text), Blank(DescriptionBox.Text));
        DialogResult = true;
    }

    private static string? Blank(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <inheritdoc/>
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
