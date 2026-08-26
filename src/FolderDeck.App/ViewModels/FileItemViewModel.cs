using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.ViewModels;

/// <summary>파일 목록의 한 줄. 열거 결과를 표시용으로 감싼다.</summary>
/// <remarks>
/// 표시 문자열은 프로퍼티에서 계산한다. 목록 가상화 덕분에 화면에 보이는 줄만
/// 실제로 계산되므로, 로그 수천 개짜리 폴더에서도 앞당겨 포맷하지 않는다.
/// </remarks>
public sealed class FileItemViewModel(FolderItem item, string relativeFolder = "")
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public FolderItem Item { get; } = item;

    /// <summary>
    /// 재귀 검색 결과의 위치 — 검색 기준 폴더를 뺀 상대 폴더. 평소 목록에서는 빈 문자열이다.
    /// </summary>
    public string RelativeFolder { get; } = relativeFolder;

    public bool HasRelativeFolder => RelativeFolder.Length > 0;

    /// <summary>list 뷰에서 이름 앞에 흐리게 붙이는 접두사.</summary>
    public string RelativeFolderPrefix =>
        HasRelativeFolder ? RelativeFolder + System.IO.Path.DirectorySeparatorChar : string.Empty;

    public string Name => Item.Name;

    public string FullPath => Item.FullPath;

    public bool IsDirectory => Item.IsDirectory;

    /// <summary>
    /// 폴더 대 파일, 두 갈래뿐이다 — 확장자별로 늘리지 않는다(§1.2). 셸 아이콘·아이콘 폰트·
    /// 이미지 리소스를 쓰지 않고 <b>유니코드 글리프만</b> 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>doc/FolderDeck_familylook_spec.md</c> §6 어휘표 그대로다 —
    /// 폴더 <c>📁</c>(U+1F4C1) · 파일 <c>📄</c>(U+1F4C4).
    /// <para>
    /// Phase 8-B ③ 에서 폴더를 <c>▣</c>(U+25A3) 로 바꿨다가 <b>8-C ① 에서 되돌렸다</b>.
    /// 바꾼 이유였던 "둘이 잘 안 갈린다"는 여전히 사실이다 — WPF 는 컬러 폰트를 그리지 않아
    /// 두 이모지가 같은 굵기의 가는 윤곽선으로 떨어진다. 다만 <b>그것을 형태가 아니라 색으로
    /// 푼다</b>: 어휘표를 지키면서 가르는 쪽이 낫다. 색은 뷰가 준다 —
    /// <c>Views/FolderPanelView.xaml</c> 의 <c>ItemGlyph</c> 스타일이
    /// <c>Brush.FolderGlyph</c>(황토)와 <c>Brush.FileGlyph</c>(무채색)를 갈라 붙인다.
    /// </para>
    /// <para>
    /// <b>색만으로 나르는 것이 아니다.</b> "색만으로 구분하지 않는다"는 규칙(설계 §3.1 의
    /// <c>열려 있음</c> 배지 · §3.2 의 상시/순환 테두리)은 <b>글자가 없는 상태 표시</b>에
    /// 대한 것이다. 여기는 글리프 옆에 이름이 늘 붙어 있고, 상세 뷰에서는 크기 칸이 폴더만
    /// 비어 있어(<see cref="SizeText"/>) 색을 못 봐도 종류를 읽을 수 있다.
    /// </para>
    /// </remarks>
    public string Glyph => Item.IsDirectory ? "📁" : "📄";

    /// <summary>폴더는 비워 둔다 — 재귀 계산하지 않는다.</summary>
    public string SizeText => Item.IsDirectory ? string.Empty : Humanize(Item.Size);

    public string ModifiedText => Item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string Humanize(long? bytes)
    {
        if (bytes is null)
        {
            return string.Empty;
        }

        double size = bytes.Value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes.Value:N0} B" : $"{size:0.#} {Units[unit]}";
    }
}
