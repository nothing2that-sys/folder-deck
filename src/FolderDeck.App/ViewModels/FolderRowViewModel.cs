using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;




public sealed partial class FolderRowViewModel(FolderEntry entry) : ObservableObject, IFileDropTarget
{
    public FolderEntry Entry { get; } = entry;


    [ObservableProperty]
    private bool isDropTarget;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropTargetText))]
    private bool dropTargetCopies;


    public string DropTargetText => DropTargetCaption.For(DropTargetCopies);

    public string DisplayName => string.IsNullOrWhiteSpace(Entry.DisplayName)
        ? System.IO.Path.GetFileName(Entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Entry.Path
        : Entry.DisplayName!;

    public string PathText => Entry.Path;

    public string? Description => Entry.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);









    public bool Pinned => HasPinnedTile;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocationHint))]
    private string? locationHint;

    public bool HasLocationHint => !string.IsNullOrEmpty(LocationHint);





    [ObservableProperty]
    private bool isInaccessible;


    [ObservableProperty]
    private bool isShowingInRotating;




    public int RotationSlot => Entry.RotationSlot ?? 1;

    public string RotationSlotText => $"순환{RotationSlot}";





    [ObservableProperty]
    private bool showRotationTag;





    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlaceInGrid))]
    [NotifyPropertyChangedFor(nameof(Pinned))]
    private bool hasPinnedTile;


    public bool CanPlaceInGrid => !HasPinnedTile;


    [ObservableProperty]
    private IReadOnlyList<RotationChoiceViewModel> rotationChoices = [];

    public void NotifyRotationSlot()
    {
        OnPropertyChanged(nameof(RotationSlot));
        OnPropertyChanged(nameof(RotationSlotText));
    }











    public void NotifyEdited()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(PathText));
    }
}
