using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 폴더 행의 우클릭 메뉴 "순환 칸 지정 ▸ 순환1 / 순환2 …" 항목 하나 (Phase 2).
/// </summary>
/// <remarks>
/// 커맨드를 항목이 직접 들고 있다 — 메뉴 항목에서 뷰모델 루트까지 XAML 로 거슬러 올라가는
/// 바인딩은 <c>ContextMenu</c> 가 비주얼 트리 밖에 있어 잘 끊긴다.
/// </remarks>
public sealed partial class RotationChoiceViewModel(
    FolderRowViewModel row,
    int slot,
    Action<RotationChoiceViewModel> apply) : ObservableObject
{
    private readonly Action<RotationChoiceViewModel> _apply = apply;

    public FolderRowViewModel Row { get; } = row;

    /// <summary>순환 번호. 1 은 기본값이라 저장할 때 null 로 눕는다.</summary>
    public int Slot { get; } = slot;

    public string Label => $"순환{Slot}";

    /// <summary>이 번호의 순환 칸이 배치에 실제로 있는가. 없으면 눌러도 폴백된다고 알린다.</summary>
    public bool Exists { get; init; } = true;

    public string? Note => Exists ? null : "(칸 없음 — 순환1로 폴백)";

    [ObservableProperty]
    private bool isSelected;

    [RelayCommand]
    private void Select() => _apply(this);
}
