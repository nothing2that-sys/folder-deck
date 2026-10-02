using CommunityToolkit.Mvvm.ComponentModel;

namespace FolderDeck.App.ViewModels;

public sealed partial class GridCellViewModel(int x, int y) : ObservableObject
{
    public int X { get; } = x;

    public int Y { get; } = y;

    [ObservableProperty]
    private bool isFree;

    [ObservableProperty]
    private bool isDropTarget;
}
