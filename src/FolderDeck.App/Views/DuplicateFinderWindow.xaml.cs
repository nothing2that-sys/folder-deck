using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

/// <summary>
/// 그룹 정체성 + 표시 문자열을 한 값에 담는다 — <see cref="PropertyGroupDescription"/>이
/// 이 값 자체로 같다/다르다를 가르므로, <b>정확한(사람이 읽기 좋게 뭉뚱그리지 않은) 바이트
/// 수</b>로 동등성을 판단해야 한다(레코드 구조체 기본 생성 <c>Equals</c>가 세 필드를 그대로
/// 비교한다). <see cref="ToString"/>만 사람이 읽는 문자열을 낸다 — WPF 는 문자열이 아닌
/// 값을 <c>TextBlock.Text</c>에 바인딩하면 <c>ToString()</c>을 부른다.
/// </summary>
/// <remarks>
/// 2026-09-18 advisor 지적: 처음에는 "이름 · 사람이 읽는 크기(Humanize) — N곳" 문자열
/// 자체로 그룹을 갈랐다 — 그런데 <c>Humanize</c>는 정밀도를 버린다(1024·1025바이트가 둘 다
/// "1 KB"). <c>DuplicateFinder</c>는 이름+<b>정확한</b> 크기로 이미 서로 다른 그룹을
/// 보장하는데, 그 뭉뚱그린 문자열로 다시 묶으면 이름이 같고 정확한 크기만 다른 두 그룹이
/// 화면에서 하나로 합쳐 보일 수 있었다.
/// </remarks>
internal readonly record struct DuplicateGroupHeading(string Name, long Size, int Count)
{
    public override string ToString() => $"{Name} · {DuplicateFinderWindow.Humanize(Size)} — {Count}곳";
}

/// <summary>표(GridView) 한 줄 — "이 사본이 어디 있는가"만 담는다. 파일명·크기는 그룹
/// 머리글이 이미 말하므로 행에서 반복하지 않는다.</summary>
internal sealed record DuplicateRow(DuplicateGroupHeading Group, string TileLabel, string FolderPath, string ModifiedText, string FullPath);

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

    /// <summary>
    /// 이 창 전용 포맷터다 — <c>internal</c>인 건 <see cref="DuplicateGroupHeading.ToString"/>이
    /// 다른 타입에서 불러야 해서지, 공유 유틸리티로 쓰라는 뜻이 아니다.
    /// <c>FileItemViewModel</c>도 따로 <c>Humanize</c>를 갖고 있는데
    /// 자릿수 구분자가 있는(<c>{bytes:N0} B</c>) 다른 형식이다 — 이 갈라짐은 이번에 만든
    /// 게 아니라 이미 있던 것이고, 이 메서드를 그쪽과 통합하는 건 이번 변경 범위 밖이다.
    /// </summary>
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
