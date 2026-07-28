using System.Windows;

namespace FolderDeck.App.Services;

/// <summary>확인창. 테스트에서 갈아끼우기 위해 인터페이스로 둔다.</summary>
public interface IUserPrompt
{
    bool Confirm(string title, string message);

    /// <summary>
    /// 되돌릴 것 없이 알리기만 한다 — 부분 실패 내역처럼 상태 바 한 줄로는 못 담는 것 (§5).
    /// </summary>
    void Report(string title, string message);
}

/// <inheritdoc/>
public sealed class UserPrompt : IUserPrompt
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    public void Report(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
