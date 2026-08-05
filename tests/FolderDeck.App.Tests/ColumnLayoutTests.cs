using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

/// <summary>
/// 상세 뷰 컬럼 자동 폭 (§3.2 · Phase 5 Stage 3D-2). <c>GridView</c> 가 star 를 지원하지 않아
/// 코드로 계산해 넣는다 — 그 계산만 여기서 덮는다.
/// </summary>
/// <remarks>
/// <b>뷰는 인스턴스화하지 않는다.</b> 테스트 어셈블리는 MTA 라 WPF 컨트롤을 만들 수 없다
/// (단계 B 실측) — 그래서 계산이 <see cref="ColumnLayout"/> 로 빠져 있다. 언제 다시 쓰는지
/// (재진입 차단 · 뷰 갈아타기)는 <c>FolderPanelView</c> 것이고 손 확인 몫이다.
/// </remarks>
public sealed class ColumnLayoutTests
{
    /// <summary>넉넉할 때 — 남는 폭을 먹고 오른쪽에 여백을 남긴다.</summary>
    [Fact]
    public void RoomySpaceGoesToTheFlexColumn()
    {
        // 실측 짝: 폭 444 목록에 세로 스크롤바가 뜨면 뷰포트 427. 고정 컬럼 합 220(크기 90 + 수정 130).
        var width = ColumnLayout.FlexWidth(427, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth);

        Assert.Equal(169, width);

        // 컬럼 합 + 행 테두리가 뷰포트에서 정확히 여백만큼 모자란다 = 가로 스크롤이 없다.
        Assert.Equal(427 - ColumnLayout.RightMargin, width + 220 + ColumnLayout.RowChrome);
    }

    /// <summary>여백은 뷰포트가 넓어져도 그대로 남는다 — 0 이 되면 단계 C 가 되돌아간다.</summary>
    [Theory]
    [InlineData(300)]
    [InlineData(427)]
    [InlineData(683)]
    [InlineData(1600)]
    public void TheRightMarginIsAlwaysKept(double viewport)
    {
        var width = ColumnLayout.FlexWidth(viewport, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth);
        var rowWidth = width + 220 + ColumnLayout.RowChrome;

        // 최소 폭에서 멈춘 경우(좁은 타일)는 행이 뷰포트를 넘는다 — 그때는 가로 스크롤이 답이다.
        if (width > ColumnLayout.MinFlexWidth)
        {
            Assert.True(
                viewport - rowWidth >= ColumnLayout.RightMargin,
                $"뷰포트 {viewport} 에서 여백이 {viewport - rowWidth} 로 줄었다");
        }
        else
        {
            Assert.True(rowWidth > viewport, "최소 폭에서 멈췄으면 가로 스크롤이어야 한다");
        }
    }

    /// <summary>좁으면 최소 폭에서 멈춘다 — 그 아래는 가로 스크롤에 맡긴다(1x1 타일).</summary>
    [Fact]
    public void NarrowTileStopsAtTheMinimum()
    {
        // 폭 260 타일의 실측 뷰포트 243. 남는 것은 243-220-32-6 = -15 다.
        Assert.Equal(
            ColumnLayout.MinFlexWidth,
            ColumnLayout.FlexWidth(243, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth));

        // 경계 바로 위/아래에서 한 번만 갈린다.
        var edge = ColumnLayout.MinFlexWidth + 220 + ColumnLayout.RightMargin + ColumnLayout.RowChrome;
        Assert.Equal(ColumnLayout.MinFlexWidth, ColumnLayout.FlexWidth(edge, 220, 32, 160));
        Assert.Equal(ColumnLayout.MinFlexWidth, ColumnLayout.FlexWidth(edge - 1, 220, 32, 160));
        Assert.Equal(ColumnLayout.MinFlexWidth + 1, ColumnLayout.FlexWidth(edge + 1, 220, 32, 160));
    }

    /// <summary>검색 뷰는 고정 컬럼 합이 다르다 — `이름` 220 + `크기` 80 + `수정` 120 = 420.</summary>
    [Fact]
    public void SearchViewHasItsOwnFixedSum()
    {
        Assert.Equal(542, ColumnLayout.FlexWidth(1000, 420, 32, 160));

        // 600px 짜리 뷰를 좁은 타일에 넣으면 `위치` 가 최소에서 멈춘다.
        Assert.Equal(160, ColumnLayout.FlexWidth(503, 420, 32, 160));
    }

    /// <summary>소수점을 남기지 않는다 — 남기면 레이아웃마다 값이 흔들려 재진입이 안 끊긴다.</summary>
    [Fact]
    public void TheResultIsAWholeNumber()
    {
        var width = ColumnLayout.FlexWidth(427.6, 220.4, 32, 160);

        Assert.Equal(width, Math.Floor(width));
        Assert.Equal(169, width);
    }

    /// <summary>레이아웃 전(폭 0 · NaN)에는 최소 폭을 낸다 — 쓸지는 뷰가 정한다.</summary>
    [Fact]
    public void BeforeLayoutItFallsBackToTheMinimum()
    {
        Assert.Equal(160, ColumnLayout.FlexWidth(0, 220, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(double.NaN, 220, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(-5, 220, 32, 160));
    }

    /// <summary>
    /// 셈이 유한하지 않으면 최소 폭으로 떨어진다 — <c>NaN</c> 을 흘리면 늘리려던 컬럼이
    /// 조용히 <c>Auto</c> 가 된다. 지금 컬럼은 전부 고정 픽셀이라 닿지 않는 자리다.
    /// </summary>
    [Fact]
    public void ANonFiniteSumFallsBackToTheMinimum()
    {
        Assert.Equal(160, ColumnLayout.FlexWidth(427, double.NaN, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(427, 220, double.NaN, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(double.PositiveInfinity, 220, 32, 160));
    }

    /// <summary>정한 값 둘 — 바뀌면 보고문의 숫자도 같이 고쳐야 한다.</summary>
    [Fact]
    public void TheChosenConstants()
    {
        Assert.Equal(32, ColumnLayout.RightMargin);
        Assert.Equal(160, ColumnLayout.MinFlexWidth);
        Assert.Equal(6, ColumnLayout.RowChrome);
    }
}
