namespace FolderDeck.App.Services;

/// <summary>
/// 클립보드 쓰기. 테스트가 STA 스레드 없이 돌 수 있게 인터페이스로 뺀다.
/// </summary>
public interface IClipboardService
{
    /// <summary>성공하면 null, 실패하면 사용자에게 보일 메시지.</summary>
    string? SetText(string text);
}

/// <inheritdoc/>
public sealed class ClipboardService : IClipboardService
{
    public string? SetText(string text)
    {
        try
        {
            // 다른 프로세스가 클립보드를 잡고 있으면 실패할 수 있다(COM 경쟁).
            // 조용히 삼키지 않고 호출부가 알리게 한다.
            System.Windows.Clipboard.SetText(text);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
