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

    /// <summary>순환 칸의 번호. 상시 칸은 null.</summary>
    public int? RotationIndex => Spec.RotationIndex;

    /// <summary>칸에 붙는 태그 — "순환2". 상시 칸은 "상시".</summary>
    public string KindTag => IsRotating ? $"순환{RotationIndex ?? 1}" : "상시";

    /// <summary>편집 모드 헤더에 쓰는 이름 — 상시는 폴더 이름, 순환은 번호 태그.</summary>
    public string EditLabel => IsRotating
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

    /// <summary>좌표·크기가 바뀌었음을 뷰에 알린다(Grid.Row/Column 바인딩).</summary>
    public void NotifyPlacement()
    {
        OnPropertyChanged(nameof(CellX));
        OnPropertyChanged(nameof(CellY));
        OnPropertyChanged(nameof(SpanX));
        OnPropertyChanged(nameof(SpanY));
        OnPropertyChanged(nameof(SizeText));
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
        Panel.EmptyTitle = IsRotating
            ? $"{KindTag} — 폴더를 지정하세요"
            : "폴더를 지정하세요";
    }
}
