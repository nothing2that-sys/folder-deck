namespace FolderDeck.App.Services;

/// <summary>폴더 선택 다이얼로그. 테스트에서 갈아끼우기 위해 인터페이스로 둔다.</summary>
public interface IFolderPicker
{
    /// <summary>사용자가 고른 폴더들. 취소하면 빈 목록.</summary>
    IReadOnlyList<string> PickFolders(string title);
}

/// <summary>
/// .NET 10 WPF 의 <see cref="Microsoft.Win32.OpenFolderDialog"/> 를 쓴다.
/// (컴파일로 존재를 확인했다 — §11 "기억으로 단정 금지".)
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    public IReadOnlyList<string> PickFolders(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = true,
        };

        return dialog.ShowDialog() == true ? dialog.FolderNames : [];
    }
}
