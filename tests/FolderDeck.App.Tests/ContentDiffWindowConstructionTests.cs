using System.Linq;
using System.Windows.Controls;
using FolderDeck.App.Views;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="ContentDiffWindow"/>를 실제로 생성해본다(<see cref="StaWpfTestSupport"/>의
/// STA 요령) — 특히 2026-09-21 JH 지적("노란색으로만 표시되면 뭐가 다른지 확인이 안
/// 된다")으로 줄 전체 배경 대신 단어 단위 토막(<see cref="ContentDiffSpan"/>)으로
/// 바꾼 뒤의 XAML 배선(중첩 <c>ItemsControl</c>)이 죽지 않는지가 핵심이다.
/// </summary>
public sealed class ContentDiffWindowConstructionTests
{
    [Fact]
    public void ConstructingWithNoDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb");
            _ = new ContentDiffWindow("shared.txt", "코드", "산출물", diff);
        });
    }

    [Fact]
    public void ConstructingWithLineLevelDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb\nc");
            _ = new ContentDiffWindow("shared.txt", "코드", "산출물", diff);
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
            var window = new ContentDiffWindow("shared.txt", "코드", "산출물", diff);
            var list = (ListView)window.FindName("RowList")!;

            var row = (ContentDiffRow)Assert.Single(list.Items.Cast<object>());

            Assert.True(row.LeftSpans.Count > 1);
            Assert.True(row.RightSpans.Count > 1);
            Assert.Contains(row.LeftSpans, s => s.Background != System.Windows.Media.Brushes.Transparent);
        });
    }
}
