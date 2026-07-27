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

    /// <summary>v1 은 확장자 기반 단순 글리프다(셸 아이콘은 후순위, §1.2).</summary>
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
