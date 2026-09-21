using System.Linq;
using System.Windows.Controls;
using FolderDeck.App.Views;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="ContentDiffWindow"/>를 실제로 생성해본다(<see cref="StaWpfTestSupport"/>의
/// STA 요령). 2026-09-21 두 차례 JH 지적을 거쳤다 — 처음엔 단어 단위 하이라이트
/// (<see cref="ContentDiffSpan"/>), 다음엔 "왼쪽/오른쪽이 각자 가로 스크롤, 세로는
/// 공유"로 화면 자체를 <c>LeftScroll</c>/<c>RightScroll</c> 두 <c>ScrollViewer</c>로
/// 다시 짰다 — 이 시험은 그 배선이 죽지 않는지가 핵심이다.
/// </summary>
public sealed class ContentDiffWindowConstructionTests
{
    [Fact]
    public void ConstructingWithNoDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb");
            _ = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
        });
    }

    [Fact]
    public void ConstructingWithLineLevelDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb\nc");
            _ = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
        });
    }

    /// <summary>
    /// 바뀐 줄이 있으면 단어 단위로 토막이 여러 개 나와야 한다 — 토막이 하나뿐이면(줄
    /// 전체를 한 토막으로 뭉갠 옛 동작으로 되돌아간 것이라) 이 시험이 잡아낸다.
    /// </summary>
    [Fact]
    public void ModifiedLineProducesMultipleSpansNotOneWholeLineSpan()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("the quick fox", "the slow fox");
            var window = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
            var leftList = (ItemsControl)window.FindName("LeftList")!;
            var rightList = (ItemsControl)window.FindName("RightList")!;

            var leftRow = (ContentDiffRow)Assert.Single(leftList.Items.Cast<object>());
            var rightRow = (ContentDiffRow)Assert.Single(rightList.Items.Cast<object>());

            Assert.True(leftRow.LeftSpans.Count > 1);
            Assert.True(rightRow.RightSpans.Count > 1);
            Assert.Contains(leftRow.LeftSpans, s => s.Background != System.Windows.Media.Brushes.Transparent);
        });
    }

    /// <summary>
    /// 헤더가 실제 경로를 보여주는지 — 2026-09-21 지적("왼쪽이 어떤 파일인지 표시가
    /// 애매함")의 핵심 수정. 이름표(타일 라벨)만 있던 걸 실제 경로까지 붙였다.
    /// </summary>
    [Fact]
    public void HeadersShowTheActualFilePath()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a", "a");
            var window = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });

            var leftHeader = (TextBlock)window.FindName("LeftHeaderText")!;
            var rightHeader = (TextBlock)window.FindName("RightHeaderText")!;

            Assert.Contains(@"C:\code\shared.txt", leftHeader.Text);
            Assert.Contains(@"C:\out\shared.txt", rightHeader.Text);
        });
    }

    // 헤더를 누르면 그 파일이 열리는지(2026-09-21 JH 요청)는 여기서 시험하지 않는다 —
    // MouseLeftButtonUp 은 MouseButtonEventArgs 를 요구하는데 그건 실제 MouseDevice가
    // 있어야 만들어진다(진짜 마우스 입력 없이 그럴듯하게 지어내기 어렵다) — 더블클릭이
    // ClickCount 라우팅을 요구해 시험 못 한다고 적어 둔 DuplicateFinderWindowConstructionTests
    // 의 이유와 같다. 생성자가 openFile 콜백을 받아 필드에 저장하고 두 헤더에 거는 것까지는
    // 위 시험들이 이미 확인한다 — 실제로 눌렸을 때 그 콜백이 옳은 경로로 불리는지는 손 확인 몫.
}
