using CommunityToolkit.Mvvm.ComponentModel;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 그리드의 1x1 칸 하나 (배치 편집 모드에서만 보인다). 격자 경계를 그리는 자리이자
/// 빈 칸에 폴더를 넣는 과녁이다 — 드래그드롭과 클릭 둘 다 받는다.
/// </summary>
public sealed partial class GridCellViewModel(int x, int y) : ObservableObject
{
    public int X { get; } = x;

    public int Y { get; } = y;

    /// <summary>타일이 덮고 있지 않은 칸. 빈 칸만 클릭·드롭을 받는다.</summary>
    [ObservableProperty]
    private bool isFree;

    /// <summary>드래그가 이 칸 위에 있는가 (드롭 과녁 강조).</summary>
    [ObservableProperty]
    private bool isDropTarget;
}
