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

    /// <summary>
    /// 폴더를 파일보다 항상 위에 둘지 (schemaVersion 6 · Phase 8-B ② → 8-E ①).
    /// </summary>
    /// <remarks>
    /// <b>정렬 슬롯 셋과 같은 층이다</b> — <see cref="ViewMode"/>·<see cref="SortBy"/>·
    /// <see cref="SortDesc"/> 가 이미 폴더마다인 것과 같은 이유로 폴더마다다: 로그 폴더는
    /// 수정 시각으로 섞어 보고 싶고 소스 폴더는 폴더가 위에 뭉쳐 있는 편이 낫다. 워크스페이스
    /// 하나로 묶으면 그 둘을 같이 만족시킬 수 없다.
    /// <para>
    /// <b>기본값이 <c>true</c> 라 초기화자로 준다.</b> 키가 없는 구파일은 이 값이 그대로 남아
    /// 「폴더 먼저」로 읽힌다 — 8-E 전의 동작이다. <c>bool?</c> 로 두지 않은 이유는 형제 셋이
    /// 전부 값 타입이고 파일에 늘 적히기 때문이다(<c>rotationSlot</c> 처럼 뜻 없는 슬롯을
    /// 감추는 자리가 아니다).
    /// </para>
    /// </remarks>
    public bool FoldersFirst { get; set; } = true;

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
