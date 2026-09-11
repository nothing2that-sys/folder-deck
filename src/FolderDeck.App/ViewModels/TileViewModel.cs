using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 그리드 칸 하나 (§3.2 오른쪽 컬럼 · Phase 2). <see cref="TileSpec"/> 은 저장 대상이고,
/// 그 안에 뜨는 <see cref="Panel"/> 의 현재 경로는 런타임 전용이다(§4.6).
/// </summary>
/// <remarks>
/// <see cref="Spec"/> 은 <c>workspace.Tiles</c> 안의 그 객체 자체다 — 여기서 좌표를 바꾸면
/// 저장 대상이 같이 바뀌고, <see cref="MainViewModel"/> 이 디바운스 저장을 예약한다.
/// </remarks>
public sealed partial class TileViewModel : ObservableObject
{
    public TileViewModel(TileSpec spec, FolderPanelViewModel panel)
    {
        Spec = spec;
        Panel = panel;
        SyncPanel();
    }

    public TileSpec Spec { get; }

    public FolderPanelViewModel Panel { get; }

    public int CellX => Spec.CellX;

    public int CellY => Spec.CellY;

    public int SpanX => Spec.SpanX;

    public int SpanY => Spec.SpanY;

    public bool IsRotating => Spec.Kind == TileKind.Rotating;

    public bool IsPinned => Spec.Kind == TileKind.Pinned;

    /// <summary>전역 검색 타일인가(D69-e 6-3).</summary>
    public bool IsSearch => Spec.Kind == TileKind.Search;

    /// <summary>순환 칸의 번호. 상시 칸·검색 칸은 null.</summary>
    public int? RotationIndex => Spec.RotationIndex;

    /// <summary>칸에 붙는 태그 — "순환2". 상시 칸은 "상시", 검색 칸은 "검색".</summary>
    public string KindTag => IsSearch ? "검색" : IsRotating ? $"순환{RotationIndex ?? 1}" : "상시";

    /// <summary>편집 모드 헤더에 쓰는 이름 — 상시는 폴더 이름, 순환·검색은 태그만.</summary>
    public string EditLabel => IsRotating || IsSearch
        ? KindTag
        : $"{KindTag} · {(string.IsNullOrEmpty(Panel.AnchorName) ? "(폴더 없음)" : Panel.AnchorName)}";

    public string SizeText => $"{SpanX}×{SpanY}";

    /// <summary>드래그로 잡는 중 — 미리보기가 뜨는 동안 원래 타일을 흐리게 한다.</summary>
    [ObservableProperty]
    private bool isDragging;

    /// <summary>
    /// 배치 편집 모드인가. <see cref="MainViewModel"/> 이 모든 타일에 같은 값을 밀어 넣는다 —
    /// DataTemplate 안에서는 뷰모델 루트로 거슬러 올라가는 바인딩이 닿지 않기 때문이다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewMode))]
    private bool isEditing;

    /// <summary>파일 목록이 살아 있는가 — 편집 모드에서는 잠근다.</summary>
    public bool IsViewMode => !IsEditing;

    /// <summary>
    /// 격자 전체로 확장된 타일인가(Phase 19 · D116). <see cref="MainViewModel"/> 이 모든
    /// 타일에 같은 판정을 밀어 넣는다 — <see cref="isEditing"/> 과 같은 관례다. 저장하지
    /// 않는 런타임 값이라 <see cref="Spec"/> 은 건드리지 않는다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCellX))]
    [NotifyPropertyChangedFor(nameof(EffectiveCellY))]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanX))]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanY))]
    private bool isExpanded;

    partial void OnIsExpandedChanged(bool value) => Panel.IsExpanded = value;

    /// <summary>다른 타일이 확장 중이라 이 타일을 접어야 하는가(Phase 19 · D116).</summary>
    [ObservableProperty]
    private bool isHiddenByExpansion;

    /// <summary>
    /// 확장됐을 때 받을 열 폭 — <see cref="MainViewModel"/> 이 격자 열 수를 밀어 넣는다
    /// (Phase 19 · D116). 타일 자신은 격자 크기를 모른다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanX))]
    private int expandedSpanX;

    /// <summary>확장됐을 때 받을 행 높이 — <see cref="ExpandedSpanX"/> 와 같은 이유(Phase 19 · D116).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanY))]
    private int expandedSpanY;

    /// <summary>
    /// 화면(<c>Grid.Column</c>)이 실제로 쓰는 열 — 확장 중이면 0, 아니면 저장값
    /// (Phase 19 · D116). <see cref="Spec"/> 의 <see cref="CellX"/> 는 그대로 둔다.
    /// </summary>
    public int EffectiveCellX => IsExpanded ? 0 : CellX;

    /// <summary>화면이 실제로 쓰는 행 — <see cref="EffectiveCellX"/> 와 같은 이유(Phase 19 · D116).</summary>
    public int EffectiveCellY => IsExpanded ? 0 : CellY;

    /// <summary>화면이 실제로 쓰는 열 폭 — 확장 중이면 격자 전체, 아니면 저장값(Phase 19 · D116).</summary>
    public int EffectiveSpanX => IsExpanded ? ExpandedSpanX : SpanX;

    /// <summary>화면이 실제로 쓰는 행 높이 — <see cref="EffectiveSpanX"/> 와 같은 이유(Phase 19 · D116).</summary>
    public int EffectiveSpanY => IsExpanded ? ExpandedSpanY : SpanY;

    /// <summary>좌표·크기가 바뀌었음을 뷰에 알린다(Grid.Row/Column 바인딩).</summary>
    public void NotifyPlacement()
    {
        OnPropertyChanged(nameof(CellX));
        OnPropertyChanged(nameof(CellY));
        OnPropertyChanged(nameof(SpanX));
        OnPropertyChanged(nameof(SpanY));
        OnPropertyChanged(nameof(SizeText));

        // 확장 중이 아닐 때도 Grid.Column/Row 바인딩은 Effective* 를 본다(§4-4) — 저장값이
        // 바뀌면 이 넷도 같이 알려야 화면이 따라온다.
        OnPropertyChanged(nameof(EffectiveCellX));
        OnPropertyChanged(nameof(EffectiveCellY));
        OnPropertyChanged(nameof(EffectiveSpanX));
        OnPropertyChanged(nameof(EffectiveSpanY));
    }

    /// <summary>성격·번호가 바뀌었음을 뷰에 알리고 패널의 테두리/빈 상태 문구를 맞춘다.</summary>
    public void NotifyKind()
    {
        SyncPanel();
        OnPropertyChanged(nameof(IsRotating));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(RotationIndex));
        OnPropertyChanged(nameof(KindTag));
        OnPropertyChanged(nameof(EditLabel));
    }

    private void SyncPanel()
    {
        Panel.IsRotating = IsRotating;

        // 검색 타일에는 EmptyTitle 을 세우지 않는다(D69-e 6-3) — IsEmpty 가 늘 false 라 뜰 일이
        // 없는 문구다.
        if (!IsSearch)
        {
            Panel.EmptyTitle = IsRotating
                ? $"{KindTag} — 폴더를 지정하세요"
                : "폴더를 지정하세요";
        }
    }
}
