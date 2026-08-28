namespace FolderDeck.App.Services;

/// <summary>
/// 설정 창 「아이콘」 탭 (D59). 테스트에서 갈아끼우기 위해 인터페이스로 둔다 —
/// <see cref="IFolderEditor"/>·<see cref="IMacroEditor"/> 와 같은 결이다.
/// </summary>
public interface IIconSettingsEditor
{
    /// <summary>
    /// 창을 띄우고 결과를 돌려준다. <b>취소면 null</b>.
    /// </summary>
    /// <param name="current">지금 저장된 매핑(<see cref="Core.Models.AppSettings.ExtensionGlyphs"/>).</param>
    IReadOnlyDictionary<string, string>? Edit(IReadOnlyDictionary<string, string> current);
}

/// <summary>
/// 설정 창을 모르는 기존 시험이 안전하게 쓸 기본값 — 항상 취소로 답한다.
/// <see cref="FolderDeck.App.ViewModels.MainViewModel"/> 이 <c>iconSettingsEditor</c> 인자
/// 없이 만들어질 때만 쓰인다.
/// </summary>
internal sealed class NullIconSettingsEditor : IIconSettingsEditor
{
    public static readonly NullIconSettingsEditor Instance = new();

    private NullIconSettingsEditor()
    {
    }

    public IReadOnlyDictionary<string, string>? Edit(IReadOnlyDictionary<string, string> current) => null;
}
