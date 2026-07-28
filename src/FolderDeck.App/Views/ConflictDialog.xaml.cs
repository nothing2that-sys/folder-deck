using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

/// <summary>
/// 동일 이름 충돌을 어떻게 할지 묻는 창 (§4.3 · Phase 3 Stage 4).
/// </summary>
/// <remarks>
/// 겹치는 것이 하나라도 있을 때만 <see cref="UserPrompt.AskConflict"/> 가 띄운다.
/// 창은 답만 담아 돌려주고 파일은 건드리지 않는다 — 실행은 엔진의 몫이다.
/// </remarks>
public partial class ConflictDialog : Window
{
    public ConflictDialog(string headline, string message, ConflictPolicy suggested)
    {
        InitializeComponent();

        HeadlineText.Text = headline;
        MessageText.Text = message;

        // Enter 로 눌리는 단추는 지금 정책에 맞춘다 — 손이 기억하는 대로 눌러도 같은 결과.
        var initial = suggested switch
        {
            ConflictPolicy.Skip => SkipButton,
            ConflictPolicy.Rename => RenameButton,
            _ => OverwriteButton,
        };

        initial.IsDefault = true;
        Loaded += (_, _) => initial.Focus();
    }

    /// <summary>사용자의 답. 취소했으면 창이 <c>false</c> 로 닫히므로 읽지 않는다.</summary>
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
