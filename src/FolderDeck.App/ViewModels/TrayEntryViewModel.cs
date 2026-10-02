using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;










public sealed partial class TrayEntryViewModel : ObservableObject, IFileDropTarget
{
    private readonly Action<TrayEntryViewModel> _remove;

    public TrayEntryViewModel(FolderEntry entry, Action<TrayEntryViewModel> remove)
    {
        Entry = entry;
        _remove = remove;
    }

    public FolderEntry Entry { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Entry.DisplayName)
        ? System.IO.Path.GetFileName(Entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Entry.Path
        : Entry.DisplayName!;

    public string PathText => Entry.Path;


    [ObservableProperty]
    private bool isChecked = true;






    [ObservableProperty]
    private bool isDropTarget;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropTargetText))]
    private bool dropTargetCopies;


    public string DropTargetText => DropTargetCaption.For(DropTargetCopies);

    [RelayCommand]
    private void Remove() => _remove(this);


    public void NotifyEdited() => OnPropertyChanged(nameof(DisplayName));
}
