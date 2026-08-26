using System.Windows;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

/// <summary>
/// 상세 뷰(<c>GridView</c>) 컬럼 머리를 <b>누르는 자리</b>로 만든다 (§3.2 · Phase 8-B ①).
/// </summary>
/// <remarks>
/// 컬럼이 어느 정렬 기준에 해당하는지를 XAML 에 적어 두는 자리다. <see cref="ColumnLayout"/> 의
/// <c>IsFlexible</c> 과 같은 결이고 같은 이유다 — <b>인덱스나 머리글 글자로 찾지 않는다</b>.
/// 머리글 글자는 화살표가 붙었다 떨어졌다 하므로(정렬 컬럼 표시) 그것으로 컬럼을 알아보면
/// 화살표가 붙는 순간 못 찾는다.
/// <para>
/// 값이 없는 컬럼(검색 뷰의 「위치」)은 눌러도 아무 일도 일어나지 않는다 — 그 컬럼에는 대응하는
/// <see cref="SortBy"/> 가 없다. 실제로 누름을 받는 자리는
/// <c>Views/FolderPanelView.xaml.cs</c> 의 <c>OnColumnHeaderClick</c> 하나뿐이고,
/// 거기서도 <b>새 경로를 만들지 않고</b> 헤더 메타의 정렬 글자가 쓰던 <c>SetSortCommand</c> 를
/// 그대로 부른다 — 같은 뜻에 두 규칙이 생기지 않게.
/// </para>
/// </remarks>
public static class ColumnSort
{
    /// <summary>이 컬럼 머리를 누르면 걸릴 정렬 기준. 안 붙였으면 <c>null</c> = 안 눌린다.</summary>
    public static readonly DependencyProperty SortKeyProperty =
        DependencyProperty.RegisterAttached(
            "SortKey", typeof(SortBy?), typeof(ColumnSort), new PropertyMetadata(null));

    public static void SetSortKey(DependencyObject target, SortBy? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SortKeyProperty, value);
    }

    public static SortBy? GetSortKey(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (SortBy?)target.GetValue(SortKeyProperty);
    }
}
