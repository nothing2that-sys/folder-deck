namespace FolderDeck.Core.Models;

/// <summary>
/// 워크스페이스에 등록된 폴더 하나 (§2.2 / §7.1 <c>folders[]</c>).
/// <para>
/// <see cref="Path"/>는 <b>앵커</b>다. 타일이 하위로 진입해도 바뀌지 않는다(§4.6).
/// 현재 경로·선택 상태·접근 가능 여부는 런타임 전용이라 이 모델에 두지 않는다(§7.1).
/// </para>
/// </summary>
public sealed class FolderEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>앵커 경로. 필수.</summary>
    public string Path { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public FolderViewMode ViewMode { get; set; } = FolderViewMode.List;

    public SortBy SortBy { get; set; } = SortBy.Name;

    public bool SortDesc { get; set; }

    /// <summary>상시(고정) 칸 대상인지 (§2.3).</summary>
    public bool Pinned { get; set; }

    /// <summary>
    /// 이 폴더를 왼쪽 리스트에서 클릭했을 때 뜰 <b>순환 칸 번호</b> (schemaVersion 3).
    /// null 이면 순환1(기본)이다 — 그래서 대부분의 폴더는 이 슬롯이 파일에 없다.
    /// </summary>
    /// <remarks>
    /// 지정한 번호의 순환 칸이 배치에 없으면 순환1로 폴백한다(있는 칸 중 가장 낮은 번호).
    /// 순환 칸을 지우면 그 번호를 가리키던 폴더는 순환1로 내려간다 — 미아를 만들지 않는다.
    /// </remarks>
    public int? RotationSlot { get; set; }
}
