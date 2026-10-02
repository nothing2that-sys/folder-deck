using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FolderDeck.App.ViewModels;

public sealed partial class RotationChoiceViewModel(
    FolderRowViewModel row,
    int slot,
    Action<RotationChoiceViewModel> apply) : ObservableObject
{
    private readonly Action<RotationChoiceViewModel> _apply = apply;

    public FolderRowViewModel Row { get; } = row;

    public int Slot { get; } = slot;

    public string Label => $"순환{Slot}";

    public bool Exists { get; init; } = true;

    public string? Note => Exists ? null : "(칸 없음 — 순환1로 폴백)";

    [ObservableProperty]
    private bool isSelected;

    [RelayCommand]
    private void Select() => _apply(this);
}
