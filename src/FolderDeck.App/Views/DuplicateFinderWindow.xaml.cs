using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

/// <summary>중복 파일 찾기 결과 창(기능 후보 ②, 2026-09-18 설계). 비모달 — <c>Show()</c>로 연다.</summary>
public partial class DuplicateFinderWindow : Window
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public DuplicateFinderWindow(IReadOnlyList<DuplicateGroup> groups, Action<string> revealInExplorer)
    {
        InitializeComponent();

        SummaryText.Text = groups.Count == 0
            ? "이름·크기가 같은 후보 중복을 찾지 못했다."
            : $"후보 중복 {groups.Count}묶음 — 이름+크기만 본 것이라 내용까지 같다는 보장은 없다.";

        foreach (var group in groups)
        {
            var groupItem = new TreeViewItem
            {
                Header = $"{group.Name} ({Humanize(group.Size)}) — {group.Entries.Count}곳",
                IsExpanded = true,
            };

            foreach (var entry in group.Entries)
            {
                var entryItem = new TreeViewItem { Header = $"{entry.TileLabel} — {entry.Item.FullPath}" };
                entryItem.MouseDoubleClick += (_, e) =>
                {
                    revealInExplorer(entry.Item.FullPath);
                    e.Handled = true;
                };
                groupItem.Items.Add(entryItem);
            }

            ResultTree.Items.Add(groupItem);
        }
    }

    private static string Humanize(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {Units[0]}" : $"{value:0.#} {Units[unit]}";
    }
}
