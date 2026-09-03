using System.Windows.Controls;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

/// <summary>
/// 상세 뷰(<c>GridView</c>) 컬럼의 <b>표시/숨김 판정</b> (§3.2 · Phase 15 · D79 · C-4).
/// </summary>
/// <remarks>
/// 판정은 <see cref="ShouldShow"/> 하나뿐이고 <b>상태도 WPF 요소도 만들지 않는다</b> — 뷰 밖에서
/// 시험할 수 있게 <see cref="ColumnLayout"/> 과 같은 결로 뺐다(테스트 어셈블리는 MTA 라 실제
/// <c>GridView</c>·<c>ListView</c> 는 못 만들지만 <c>GridViewColumn</c> 은 만들 수 있다). 언제
/// 다시 적용하는지, 원본 컬럼 인스턴스를 어떻게 간수하다 제자리에 꽂는지는
/// <see cref="FolderPanelView"/> 것이다.
/// </remarks>
public static class ColumnVisibility
{
    /// <summary>
    /// 이 컬럼이 지금 보여야 하는가. <c>Header</c> 글자로 가르지 않는다 — 이미 있는 첨부
    /// 프로퍼티 <see cref="ColumnSort.SortKey"/> 로 가른다(<see cref="ColumnSort"/> 가 화살표
    /// 때문에 헤더 글자로 못 찾는 것과 같은 이유).
    /// </summary>
    /// <remarks>
    /// `이름` 은 끌 수 없다(D79b-7) — <c>SortKey</c> 가 <see cref="SortBy.Name"/> 이거나 그 밖의
    /// 알려지지 않은 값이면 무조건 보인다. `위치` 열에는 <c>SortKey</c> 자체가 없다
    /// (<c>FolderPanelView.xaml</c> 의 「위치」 컬럼 위 주석 — <c>SortBy</c> 열거자에 경로 값이
    /// 없어서다) — <c>null</c> 이 곧 그 열이라는 뜻이다.
    /// </remarks>
    public static bool ShouldShow(GridViewColumn column, bool showSize, bool showModified, bool showPosition) =>
        ColumnSort.GetSortKey(column) switch
        {
            SortBy.Size => showSize,
            SortBy.Modified => showModified,
            null => showPosition,
            _ => true,
        };
}
