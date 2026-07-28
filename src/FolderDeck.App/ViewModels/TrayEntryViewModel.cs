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
public sealed partial class TrayEntryViewModel : ObservableObject
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

    [RelayCommand]
    private void Remove() => _remove(this);
}
