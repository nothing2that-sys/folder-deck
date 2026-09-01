namespace FolderDeck.App.Services;

/// <summary>
/// 설정 창에 넘기는 요청 한 벌 (D69-d 결정 2) — 「아이콘」·「검색」 두 탭이 함께 쓰는 값을
/// 레코드로 묶는다. 인자를 개별로 늘리면 값이 붙을 때마다 시그니처가 또 바뀐다.
/// </summary>
public sealed record SettingsEditRequest(
    IReadOnlyDictionary<string, string> ExtensionGlyphs,
    bool ShowShellIcons,
    int EverythingMaxResults,
    EverythingStatus EverythingStatus);

/// <summary>창이 저장을 눌러 돌려주는 값 한 벌. 취소면 <see cref="ISettingsEditor.Edit"/> 가 <c>null</c> 을 낸다.</summary>
public sealed record SettingsEditResult(
    IReadOnlyDictionary<string, string> ExtensionGlyphs,
    bool ShowShellIcons,
    int EverythingMaxResults);

/// <summary>
/// 설정 창 (D59 「아이콘」 탭 전용으로 시작해 D69-d 가 이름·계약을 함께 넓혔다). 테스트에서
/// 갈아끼우기 위해 인터페이스로 둔다 — <see cref="IFolderEditor"/>·<see cref="IMacroEditor"/> 와
/// 같은 결이다.
/// </summary>
public interface ISettingsEditor
{
    /// <summary>
    /// 창을 띄우고 결과를 돌려준다. <b>취소면 null</b>.
    /// </summary>
    SettingsEditResult? Edit(SettingsEditRequest request);
}

/// <summary>
/// 설정 창을 모르는 기존 시험이 안전하게 쓸 기본값 — 항상 취소로 답한다.
/// <see cref="FolderDeck.App.ViewModels.MainViewModel"/> 이 <c>settingsEditor</c> 인자
/// 없이 만들어질 때만 쓰인다.
/// </summary>
internal sealed class NullSettingsEditor : ISettingsEditor
{
    public static readonly NullSettingsEditor Instance = new();

    private NullSettingsEditor()
    {
    }

    public SettingsEditResult? Edit(SettingsEditRequest request) => null;
}
