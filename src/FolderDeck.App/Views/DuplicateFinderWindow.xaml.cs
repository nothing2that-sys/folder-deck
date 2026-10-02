using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

internal readonly record struct DuplicateGroupHeading(string Name, long Size, int Count)
{
    public override string ToString() => $"{Name} · {DuplicateFinderWindow.Humanize(Size)} — {Count}곳";
}

internal sealed record DuplicateRow(DuplicateGroupHeading Group, string TileLabel, string FolderPath, string ModifiedText, string FullPath);

public partial class DuplicateFinderWindow : Window
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public DuplicateFinderWindow(IReadOnlyList<DuplicateGroup> groups, Action<string> revealInExplorer)
    {
        InitializeComponent();

        SummaryText.Text = groups.Count == 0
            ? "이름·크기가 같은 후보 중복을 찾지 못했다."
            : $"후보 중복 {groups.Count}묶음 — 이름+크기만 본 것이라 내용까지 같다는 보장은 없다.";

        var rows = groups
            .SelectMany(group =>
            {
                var heading = new DuplicateGroupHeading(group.Name, group.Size, group.Entries.Count);
                return group.Entries.Select(entry => new DuplicateRow(
                    Group: heading,
                    TileLabel: entry.TileLabel,
                    FolderPath: Path.GetDirectoryName(entry.Item.FullPath) ?? entry.Item.FullPath,
                    ModifiedText: entry.Item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    FullPath: entry.Item.FullPath));
            })
            .ToList();

        ResultList.ItemsSource = rows;
        var view = (ICollectionView)CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DuplicateRow.Group)));

        ResultList.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: DuplicateRow row })
            {
                revealInExplorer(row.FullPath);
            }
        };
    }

    internal static string Humanize(long bytes)
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
