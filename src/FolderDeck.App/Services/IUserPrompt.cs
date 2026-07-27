using System.Windows;

namespace FolderDeck.App.Services;

/// <summary>확인창. 테스트에서 갈아끼우기 위해 인터페이스로 둔다.</summary>
public interface IUserPrompt
{
    bool Confirm(string title, string message);
}

/// <inheritdoc/>
public sealed class UserPrompt : IUserPrompt
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;
}
