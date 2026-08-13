using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 복사 대상함에 상주하는 목적지 하나 (§3.2 · §4.2-1).
/// </summary>
/// <remarks>
/// <b>체크는 저장하지 않는다.</b> 저장되는 것은 "이 폴더가 대상함에 있다"는 사실
/// (<c>copyTray[]</c>)뿐이고, 체크는 런타임 상태로 <b>기본 켬</b>이다 — 대상함에 넣은
/// 이유가 목적지로 쓰려는 것이므로 매번 다시 켜게 만들 이유가 없다. 특정 실행에서 일부만
/// 보내고 싶으면 그때 끈다.
/// </remarks>
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

    /// <summary>체크된 폴더가 목적지다 (§4.2-1). 다중 체크 = 동시 복사.</summary>
    [ObservableProperty]
    private bool isChecked = true;

    /// <summary>
    /// 그리드에서 끌어온 파일이 이 항목 위에 있는가 (§4.2-2 드롭 과녁).
    /// <b>체크와 무관하다</b> — 끌어다 놓는 것은 이 항목 하나를 겨냥한 동작이라
    /// 체크된 목적지 집합(버튼 실행)과 섞이지 않는다.
    /// </summary>
    [ObservableProperty]
    private bool isDropTarget;

    [RelayCommand]
    private void Remove() => _remove(this);

    /// <summary>폴더의 표시 이름을 고쳤다 (§4.11-3) — <c>Entry</c> 에서 계산되므로 손으로 알린다.</summary>
    public void NotifyEdited() => OnPropertyChanged(nameof(DisplayName));
}
