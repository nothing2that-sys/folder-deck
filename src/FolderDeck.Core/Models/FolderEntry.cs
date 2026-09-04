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

    /// <summary>상세 뷰 `크기` 열을 보일지 (schemaVersion 8 · Phase 15 · D79).</summary>
    /// <remarks>
    /// <b>기본값이 <c>true</c> 라 초기화자로 준다</b> — <see cref="FoldersFirst"/> 와 같은 이유다.
    /// 키가 없는 구파일은 이 값이 그대로 남아 「보인다」로 읽힌다, Phase 15 전의 실제 동작이다.
    /// 폭이 아니라 표시 여부다 — 자동 폭 계산은 이 슬롯을 모른다.
    /// </remarks>
    public bool ShowSize { get; set; } = true;

    /// <summary>상세 뷰 `수정` 열을 보일지 (schemaVersion 8 · Phase 15 · D79).</summary>
    /// <remarks><see cref="ShowSize"/> 와 같은 이유·같은 기본값이다.</remarks>
    public bool ShowModified { get; set; } = true;

    /// <summary>타일 하단 미리보기 칸을 보일지 (schemaVersion 9 · Phase 17 · D86).</summary>
    /// <remarks>
    /// <b><see cref="ShowSize"/>·<see cref="FoldersFirst"/> 와 초기화자 방향이 반대인 것이
    /// 핵심이다.</b> 저 둘은 기존 동작이 「켜짐」이라 <c>= true</c> 여야 했고, 미리보기 칸은
    /// Phase 17 전에는 <b>없던 것</b>이라 <c>= false</c> 여야 구파일의 동작이 그대로다.
    /// <c>bool</c> 기본값이 마침 <c>false</c> 라 초기화자를 생략해도 동작은 같지만, 옆의 두
    /// 필드가 <c>= true</c> 인 자리에서 초기화자 없는 것만 뜻이 안 보이므로 그래도 명시한다.
    /// </remarks>
    public bool ShowPreview { get; set; } = false;

    /// <summary>범위 밖 <see cref="PreviewRatio"/> 값이 떨어지는 기본값 (schemaVersion 9).</summary>
    public const double DefaultPreviewRatio = 0.35;

    private double _previewRatio = DefaultPreviewRatio;

    /// <summary>목록+미리보기 높이 중 미리보기 몫 (schemaVersion 9 · Phase 17 · D86).</summary>
    /// <remarks>
    /// <b>초기화자가 없으면 슬롯 없는 구파일이 <c>0.0</c> 으로 읽혀 켠 순간 칸이 0 높이로
    /// 뜬다.</b> 범위(<c>0.0</c> 이상 <c>1.0</c> 이하, 양 끝 포함) 밖의 값은 가장 가까운
    /// 끝값으로 자르지 않고 <see cref="DefaultPreviewRatio"/> 로 떨어뜨린다 — <c>AppSettings</c>
    /// 의 <c>TileGap</c>·<c>EverythingMaxResults</c> 와 같은 결이다: 자료형이 달라도 저장소
    /// 안에서 같은 뜻의 장치가 두 모양이 되는 것이 더 나쁘다. <c>NaN</c> 도 범위 비교가 전부
    /// 거짓이 되어 저절로 여기로 떨어진다 — 그러려고 <c>value is &gt;= 0.0 and &lt;= 1.0</c>
    /// 형태로 쓴 것이니 다른 형태로 바꾸지 않는다.
    /// </remarks>
    public double PreviewRatio
    {
        get => _previewRatio;
        set => _previewRatio = value is >= 0.0 and <= 1.0 ? value : DefaultPreviewRatio;
    }

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
