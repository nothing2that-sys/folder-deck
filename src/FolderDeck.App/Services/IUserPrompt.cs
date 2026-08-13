using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>
/// 겹치는 이름을 어떻게 할지에 대한 사용자의 답 (§4.3).
/// </summary>
/// <param name="Policy">이번 실행에 적용할 정책.</param>
/// <param name="Remember">
/// 이 작업 관리에서 다음부터 묻지 않고 <paramref name="Policy"/> 를 쓸지.
/// </param>
public sealed record ConflictDecision(ConflictPolicy Policy, bool Remember);

/// <summary>확인창. 테스트에서 갈아끼우기 위해 인터페이스로 둔다.</summary>
public interface IUserPrompt
{
    bool Confirm(string title, string message);

    /// <summary>
    /// 되돌릴 것 없이 알리기만 한다 — 부분 실패 내역처럼 상태 바 한 줄로는 못 담는 것 (§5).
    /// </summary>
    void Report(string title, string message);

    /// <summary>
    /// 같은 이름이 이미 있을 때 어떻게 할지 묻는다 (§4.3). <b>취소면 null</b>.
    /// </summary>
    /// <remarks>
    /// 겹치는 것이 하나도 없으면 아예 부르지 않는다 — 안 겹치는 대부분의 경우에 창이 뜨면
    /// 즉흥적인 복사·드래그가 매번 막힌다. 물어볼지 말지는 부르는 쪽이 정한다.
    /// </remarks>
    ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested);

    /// <summary>
    /// 새 이름을 묻는다 (§4.6 309행 · A군 ②). <b>취소면 null</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="Confirm"/> 로는 안 된다 — 글자를 받아야 한다. <b>여기서 판정하지 않는다</b>:
    /// 빈 이름·금칙 문자·예약 이름·중복은 부르는 쪽이 <c>FolderPathRules</c> 로 보고 상태 바에
    /// 이유를 적는다(D4). 창은 사람이 적은 것을 <b>다듬지 않고</b> 그대로 돌려준다.
    /// </remarks>
    /// <param name="current">미리 채워 둘 지금 이름.</param>
    string? AskName(string title, string message, string current);
}

/// <inheritdoc/>
public sealed class UserPrompt : IUserPrompt
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    public void Report(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc/>
    /// <remarks>
    /// 선택지가 셋이라 <see cref="MessageBox"/> 로는 안 된다(버튼 조합이 정해져 있다).
    /// 그래서 작은 전용 창을 쓴다 — 버튼 글씨가 곧 동작이라 "예/아니오"보다 덜 헷갈린다.
    /// </remarks>
    public ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested)
    {
        var dialog = new ConflictDialog(title, message, suggested)
        {
            // 소유자가 있으면 그 창 가운데에 뜬다. 없으면(테스트·시작 직후) 화면 가운데.
            Owner = Application.Current?.MainWindow is { IsLoaded: true } owner ? owner : null,
        };

        if (dialog.Owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Decision : null;
    }

    /// <inheritdoc/>
    public string? AskName(string title, string message, string current)
    {
        var dialog = new RenameDialog(title, message, current)
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
