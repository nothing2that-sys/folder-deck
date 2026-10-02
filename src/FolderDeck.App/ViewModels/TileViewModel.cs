using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;









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


    public bool IsSearch => Spec.Kind == TileKind.Search;


    public int? RotationIndex => Spec.RotationIndex;


    public string KindTag => IsSearch ? "검색" : IsRotating ? $"순환{RotationIndex ?? 1}" : "상시";


    public string EditLabel => IsRotating || IsSearch
        ? KindTag
        : $"{KindTag} · {(string.IsNullOrEmpty(Panel.AnchorName) ? "(폴더 없음)" : Panel.AnchorName)}";

    public string SizeText => $"{SpanX}×{SpanY}";


    [ObservableProperty]
    private bool isDragging;





    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewMode))]
    private bool isEditing;


    public bool IsViewMode => !IsEditing;






    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCellX))]
    [NotifyPropertyChangedFor(nameof(EffectiveCellY))]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanX))]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanY))]
    private bool isExpanded;

    partial void OnIsExpandedChanged(bool value) => Panel.IsExpanded = value;


    [ObservableProperty]
    private bool isHiddenByExpansion;





    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanX))]
    private int expandedSpanX;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSpanY))]
    private int expandedSpanY;





    public int EffectiveCellX => IsExpanded ? 0 : CellX;


    public int EffectiveCellY => IsExpanded ? 0 : CellY;


    public int EffectiveSpanX => IsExpanded ? ExpandedSpanX : SpanX;


    public int EffectiveSpanY => IsExpanded ? ExpandedSpanY : SpanY;


    public void NotifyPlacement()
    {
        OnPropertyChanged(nameof(CellX));
        OnPropertyChanged(nameof(CellY));
        OnPropertyChanged(nameof(SpanX));
        OnPropertyChanged(nameof(SpanY));
        OnPropertyChanged(nameof(SizeText));



        OnPropertyChanged(nameof(EffectiveCellX));
        OnPropertyChanged(nameof(EffectiveCellY));
        OnPropertyChanged(nameof(EffectiveSpanX));
        OnPropertyChanged(nameof(EffectiveSpanY));
    }


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



        if (!IsSearch)
        {
            Panel.EmptyTitle = IsRotating
                ? $"{KindTag} — 폴더를 지정하세요"
                : "폴더를 지정하세요";
        }
    }
}
