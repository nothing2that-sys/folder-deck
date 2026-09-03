using System.Windows.Controls;
using FolderDeck.App.Views;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 상세 뷰 컬럼의 표시/숨김 판정 (§3.2 · Phase 15 · D79 · C-4). <c>GridView.Columns</c> 를
/// 실제로 갈아 끼우는 것이 아니라 그 판정 하나만 여기서 덮는다.
/// </summary>
/// <remarks>
/// <b>뷰는 인스턴스화하지 않는다.</b> 테스트 어셈블리는 MTA 라 <c>ListView</c>·<c>GridView</c> 는
/// 못 만들지만(<c>ColumnLayoutTests</c> 와 같은 사정) <c>GridViewColumn</c> 은
/// <c>DependencyObject</c> 라 만들 수 있다 — 계산이 <see cref="ColumnVisibility"/> 로 빠져 있다.
/// 언제 다시 적용하는지(<c>_flexColumn</c>/<c>_flexWidth</c> 무효화 포함)는
/// <c>FolderPanelView</c> 것이고 손 확인 몫이다.
/// </remarks>
public sealed class ColumnVisibilityCalcTests
{
    private static GridViewColumn Column(SortBy? sortKey)
    {
        var column = new GridViewColumn();
        ColumnSort.SetSortKey(column, sortKey);
        return column;
    }

    /// <summary>`이름` 은 끌 수 없다(D79b-7) — 셋 다 꺼도 항상 보인다.</summary>
    [Fact]
    public void TheNameColumnAlwaysShows()
    {
        var name = Column(SortBy.Name);

        Assert.True(ColumnVisibility.ShouldShow(name, showSize: false, showModified: false, showPosition: false));
        Assert.True(ColumnVisibility.ShouldShow(name, showSize: true, showModified: true, showPosition: true));
    }

    /// <summary>`크기` 는 <c>showSize</c> 하나만 따른다 — 나머지 둘은 무관하다.</summary>
    [Fact]
    public void TheSizeColumnFollowsOnlyShowSize()
    {
        var size = Column(SortBy.Size);

        Assert.True(ColumnVisibility.ShouldShow(size, showSize: true, showModified: false, showPosition: false));
        Assert.False(ColumnVisibility.ShouldShow(size, showSize: false, showModified: true, showPosition: true));
    }

    /// <summary>`수정` 은 <c>showModified</c> 하나만 따른다.</summary>
    [Fact]
    public void TheModifiedColumnFollowsOnlyShowModified()
    {
        var modified = Column(SortBy.Modified);

        Assert.True(ColumnVisibility.ShouldShow(modified, showSize: false, showModified: true, showPosition: false));
        Assert.False(ColumnVisibility.ShouldShow(modified, showSize: true, showModified: false, showPosition: true));
    }

    /// <summary>
    /// `위치` 열에는 <c>SortKey</c> 자체가 없다(<c>SortBy</c> 에 경로 값이 없어서다) —
    /// <c>null</c> 이 그 열을 가리키고 <c>showPosition</c> 하나만 따른다.
    /// </summary>
    [Fact]
    public void TheUnkeyedColumnIsThePositionColumnAndFollowsShowPosition()
    {
        var position = Column(sortKey: null);

        Assert.True(ColumnVisibility.ShouldShow(position, showSize: false, showModified: false, showPosition: true));
        Assert.False(ColumnVisibility.ShouldShow(position, showSize: true, showModified: true, showPosition: false));
    }

    /// <summary>가르는 것은 첨부 프로퍼티지 <c>Header</c> 글자가 아니다 — 글자를 바꿔도 안 흔들린다.</summary>
    [Fact]
    public void HeaderTextDoesNotAffectTheVerdict()
    {
        var size = Column(SortBy.Size);
        size.Header = "이 열의 이름을 뭐라 지어도";

        Assert.False(ColumnVisibility.ShouldShow(size, showSize: false, showModified: true, showPosition: true));
    }
}
