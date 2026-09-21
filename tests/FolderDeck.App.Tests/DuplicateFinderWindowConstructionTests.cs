using System.Windows.Controls;
using System.Windows.Data;
using FolderDeck.App.Views;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="DuplicateFinderWindow"/>를 실제로 생성해본다(<see cref="StaWpfTestSupport"/>의
/// STA 요령) — TreeView 를 GridView 로 다시 짠(2026-09-18, JH 손 확인 "직관적이지 않다"
/// 지적) 뒤의 회귀 시험. 그룹핑·행 평탄화(<c>PropertyGroupDescription</c>)가 실제로 죽지
/// 않는지, 그리고 <b>서로 다른 그룹이 화면에서 하나로 뭉치지 않는지</b>가 핵심이다 —
/// <c>FindDuplicateGroupsTests</c>는 그룹을 찾는 로직만 보지 창은 안 연다.
/// </summary>
public sealed class DuplicateFinderWindowConstructionTests
{
    [Fact]
    public void ConstructingWithNoGroupsDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var window = new DuplicateFinderWindow([], _ => { });
            var list = (ListView)window.FindName("ResultList")!;

            Assert.Empty(list.Items);
        });
    }

    [Fact]
    public void ConstructingWithGroupsFlattensEntriesIntoRows()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var groups = new[]
            {
                new DuplicateGroup("a.txt", 10, [
                    new DuplicateGroupEntry("코드", new FolderItem("a.txt", @"C:\code\a.txt", false, 10, DateTime.UtcNow)),
                    new DuplicateGroupEntry("산출물", new FolderItem("a.txt", @"C:\out\a.txt", false, 10, DateTime.UtcNow)),
                ]),
            };

            var window = new DuplicateFinderWindow(groups, _ => { });
            var list = (ListView)window.FindName("ResultList")!;

            Assert.Equal(2, list.Items.Count);
        });
    }

    /// <summary>
    /// 2026-09-18 advisor 지적: 화면 머리글은 크기를 사람이 읽는 단위로 뭉뚱그린다
    /// (<c>Humanize</c> — 1024·1025바이트가 둘 다 "1 KB"). 예전 구현은 그 뭉뚱그린 문자열
    /// 자체로 <c>PropertyGroupDescription</c> 을 걸어서, 이름이 같고 정확한 바이트 수만
    /// 다른 두 개의 <b>실제로는 서로 다른</b> 그룹(DuplicateFinder 는 (이름, 정확한 크기)
    /// 로 이미 갈라 놨다)이 화면에서 하나로 합쳐 보일 수 있었다. 지금은 정확한 크기로
    /// 그룹 정체성을 잡고 표시 문자열은 별도로 둔다 — 이 시험이 그 분리를 못박는다.
    /// </summary>
    [Fact]
    public void GroupsWithTheSameNameButDifferentExactSizeStayDistinctInTheView()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var groups = new[]
            {
                new DuplicateGroup("a.txt", 1024, [
                    new DuplicateGroupEntry("코드", new FolderItem("a.txt", @"C:\code\a.txt", false, 1024, DateTime.UtcNow)),
                    new DuplicateGroupEntry("산출물", new FolderItem("a.txt", @"C:\out\a.txt", false, 1024, DateTime.UtcNow)),
                ]),
                new DuplicateGroup("a.txt", 1025, [
                    new DuplicateGroupEntry("작업", new FolderItem("a.txt", @"C:\work\a.txt", false, 1025, DateTime.UtcNow)),
                    new DuplicateGroupEntry("문서", new FolderItem("a.txt", @"C:\docs\a.txt", false, 1025, DateTime.UtcNow)),
                ]),
            };

            var window = new DuplicateFinderWindow(groups, _ => { });
            var list = (ListView)window.FindName("ResultList")!;
            var view = CollectionViewSource.GetDefaultView(list.ItemsSource);

            Assert.Equal(4, list.Items.Count);
            Assert.Equal(2, view.Groups!.Count);
        });
    }

    // 더블클릭이 revealInExplorer 까지 실제로 닿는지는 여기서 시험하지 않는다 — WPF 의
    // MouseDoubleClick 은 진짜 마우스 라우팅(ClickCount==2)이 있어야 올라오는 이벤트라
    // MouseButtonEventArgs 를 그럴듯하게 지어내는 것보다 손 확인이 더 정직하다
    // (ShellMenuPolicyTests 의 COM 관련 한계 주석과 같은 결).
}
