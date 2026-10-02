using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

public sealed partial class MacroCardViewModel : ObservableObject
{
    public MacroCardViewModel(MacroDefinition definition)
    {
        Definition = definition;
    }

    public MacroDefinition Definition { get; }

    public string Name => string.IsNullOrWhiteSpace(Definition.Name)
        ? "(이름 없음)"
        : Definition.Name!;

    public string Summary
    {
        get
        {
            var source = Definition.Source?.Kind == MacroSourceKind.FixedPath
                ? LeafOf(Definition.Source.Path)
                : "고른 것";

            var op = Definition.Op switch
            {
                FileOperationKind.Move => "이동",
                FileOperationKind.Trash => "휴지통",
                _ => "복사",
            };

            if (Definition.Op == FileOperationKind.Trash)
            {
                return $"{source} → {op}";
            }

            var dest = Definition.Dest?.Kind switch
            {
                MacroDestKind.AllVisible => "보이는 모든 폴더",
                MacroDestKind.FolderIds => $"고정 폴더 {Definition.Dest.FolderIds?.Count ?? 0}개",
                _ => "대상함(체크된 것)",
            };

            var conflict = Definition.OnConflict switch
            {
                ConflictPolicy.Skip => "건너뛰기",
                ConflictPolicy.Rename => "둘 다 두기",
                _ => "덮어쓰기",
            };

            return $"{source} → {dest} · {op} · {conflict}";
        }
    }

    public bool RunsWithoutConfirm => !Definition.Confirm && Definition.Op == FileOperationKind.Copy;

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RunsWithoutConfirm));
    }

    private static string LeafOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(경로 없음)";
        }

        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
