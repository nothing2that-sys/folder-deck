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
    /// <b>폴더 쪽을 <c>📁</c>(U+1F4C1) 에서 <c>▣</c>(U+25A3) 로 바꿨다</b> (Phase 8-B ③).
    /// WPF 는 컬러 폰트를 그리지 않아 두 이모지가 <b>같은 굵기의 가는 윤곽선</b>으로 떨어진다 —
    /// 목록 글자 크기에서 <c>📁</c> 와 <c>📄</c> 의 실루엣이 거의 갈리지 않았다.
    /// <c>▣</c> 는 속이 찬 사각이라 <b>잉크의 무게</b>가 다르고, 파일 쪽 <c>📄</c> 는
    /// 세로로 긴 종이 윤곽이라 <b>실루엣</b>도 다르다. 두 축이 함께 갈리므로 훑는 눈에
    /// 폴더가 먼저 걸린다 — 목록에서 소수인 쪽이 튀어나오는 것이 맞다.
    /// <para>
    /// 색도 갈라 붙지만(폴더=본문색 / 파일=보조색) 그것은 뷰의 몫이고 <b>덤이다</b> —
    /// 색만으로 뜻을 나르지 않는다는 규칙(§3.2)대로 글리프 자체가 이미 갈린다.
    /// </para>
    /// <para>
    /// <c>doc/FolderDeck_familylook_spec.md</c> §6 어휘표는 폴더를 <c>📁</c> 로 못박고
    /// 있다 — 이 변경은 그 한 칸과 어긋난다. 문서는 이 작업 범위 밖이라 고치지 않았다.
    /// </para>
    /// </remarks>
    public string Glyph => Item.IsDirectory ? "▣" : "📄";

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
