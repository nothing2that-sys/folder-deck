using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>매크로 탭의 규칙 카드 하나 (§3.2 · §4.7).</summary>
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

    /// <summary>
    /// 카드 한 줄 요약 — "무엇 → 어디 · 동작"이 눌러 보기 전에 읽혀야 한다.
    /// </summary>
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

            // 휴지통은 목적지가 없다 — 화살표를 그리면 어디로 가는지 있는 것처럼 보인다.
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

    /// <summary>확인 없이 바로 도는 규칙인지 — 눌렀을 때 무슨 일이 날지 미리 알려 준다.</summary>
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
