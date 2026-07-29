using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 창 위치 복원과 화면 밖 방어 (§6.1 · Phase 4 Stage 2 §3.3).
/// <para>
/// 화면 구성은 이 PC 의 실측값을 그대로 쓴다(Stage 0 §1) — 주 모니터가 (0,0) 이고
/// <b>다른 모니터가 음수 좌표</b>에 있는 구성이다. <c>x &lt; 0</c> 같은 판정이 왜 안 되는지가
/// 여기 박혀 있다.
/// </para>
/// </summary>
public sealed class WindowPlacementTests
{
    /// <summary>주 모니터 2560x1440 (작업줄 48 제외).</summary>
    private static readonly ScreenRect Primary = new(0, 0, 2560, 1392);

    /// <summary>오른쪽 세로 모니터.</summary>
    private static readonly ScreenRect Right = new(2560, 108, 1080, 1872);

    /// <summary>왼쪽 세로 모니터 — 정상 위치인데 좌표가 음수다.</summary>
    private static readonly ScreenRect Left = new(-1440, -271, 1440, 2512);

    private static readonly ScreenRect[] Screens = [Primary, Right, Left];

    private static WindowSpec Spec(double x, double y, double w, double h, bool maximized = false) =>
        new() { X = x, Y = y, Width = w, Height = h, Maximized = maximized };

    private static WindowPlacementResult Resolve(WindowSpec? saved) =>
        WindowPlacement.Resolve(saved, Screens, primaryIndex: 0);

    // ── 그대로 복원되는 경우 ─────────────────────────────────────────────────

    [Fact]
    public void ASavedPositionInsideTheWorkAreaComesBackUnchanged()
    {
        var result = Resolve(Spec(300, 200, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(new ScreenRect(300, 200, 1400, 900), result.Bounds);
        Assert.False(result.Maximized);
    }

    /// <summary>
    /// 이 PC 의 왼쪽 모니터는 정상 위치가 (-1440, -271) 이다. 음수 좌표를 화면 밖으로
    /// 오판하면 멀쩡한 창이 매번 주 모니터로 끌려온다 — Stage 0 §1 이 남긴 실측 반례.
    /// </summary>
    [Fact]
    public void NegativeCoordinatesAreNotOutsideTheScreen()
    {
        var result = Resolve(Spec(-1340, -171, 1200, 2000));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(-1340, result.Bounds.X);
        Assert.Equal(-171, result.Bounds.Y);
    }

    /// <summary>두 모니터에 걸친 창 — 한쪽에서 제목줄을 잡을 수 있으면 그대로 둔다.</summary>
    [Fact]
    public void AWindowStraddlingTwoMonitorsIsKept()
    {
        var result = Resolve(Spec(2200, 300, 800, 600));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(2200, result.Bounds.X);
    }

    // ── 교차 없음 → 기본 위치 ────────────────────────────────────────────────

    [Fact]
    public void APositionThatTouchesNoMonitorFallsBackToTheCenterOfThePrimary()
    {
        var result = Resolve(Spec(-30000, -30000, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(new ScreenRect(580, 246, 1400, 900), result.Bounds);
    }

    [Fact]
    public void NoSavedPositionMeansTheDefaultPlaceAndSize()
    {
        var result = Resolve(null);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(WindowPlacement.DefaultWidth, result.Bounds.Width);
        Assert.Equal(WindowPlacement.DefaultHeight, result.Bounds.Height);
    }

    [Theory]
    [InlineData(0, 900)]        // 폭이 없다
    [InlineData(1400, 0)]       // 높이가 없다
    [InlineData(-1400, 900)]    // 손으로 고친 음수 크기
    public void ASavedSizeThatIsNotUsableFallsBack(double width, double height)
    {
        var result = Resolve(Spec(300, 200, width, height));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    // ── 1픽셀만 걸친 경우 — MonitorFromRect 만으로는 못 잡는 자리 ────────────

    /// <summary>
    /// <c>MonitorFromRect(DEFAULTTONULL)</c> 는 1픽셀만 걸쳐도 "화면 안"이라고 답한다.
    /// §6.1 이 막으려는 것은 못 찾는 창이므로 그 답으로는 부족하다.
    /// </summary>
    [Fact]
    public void AWindowOverlappingByOnePixelIsNotRecoverable()
    {
        // 오른쪽 모니터의 오른쪽 끝에서 1픽셀만 걸친다.
        var result = Resolve(Spec(Right.Right - 1, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void AWindowWhoseTitleBarIsAboveTheWorkAreaIsNotRecoverable()
    {
        // 넓게 겹치지만 제목줄이 주 모니터 위쪽 밖에 있다 — 잡을 데가 없다.
        var result = Resolve(Spec(300, -800, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    /// <summary>
    /// 위 경우와 짝. 같은 좌표라도 <b>거기에 모니터가 있으면</b> 정상이다 —
    /// 판정이 "위쪽이면 안 된다"가 아니라 "어느 작업영역의 위쪽 변 안인가"라는 확인.
    /// </summary>
    [Fact]
    public void TheSameTopEdgeIsFineWhenAMonitorActuallyStartsThere()
    {
        var result = Resolve(Spec(-1000, -271, 900, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
    }

    // ── 최소값 경계 ──────────────────────────────────────────────────────────

    // 왼쪽 모니터의 왼쪽 밖으로 창을 내보낸다. 그쪽에는 다른 모니터가 없어서
    // "옆 모니터 덕분에 통과"가 섞이지 않는다.

    [Fact]
    public void OverlapExactlyAtTheMinimumWidthIsAccepted()
    {
        var x = Left.X + WindowPlacement.MinVisibleWidth - 1400;

        var result = Resolve(Spec(x, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(x, result.Bounds.X);
    }

    [Fact]
    public void OverlapOneShortOfTheMinimumWidthIsRejected()
    {
        var x = Left.X + WindowPlacement.MinVisibleWidth - 1400 - 1;

        var result = Resolve(Spec(x, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void OverlapExactlyAtTheMinimumHeightIsAccepted()
    {
        var y = Primary.Bottom - WindowPlacement.MinVisibleHeight;

        var result = Resolve(Spec(300, y, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
    }

    [Fact]
    public void OverlapOneShortOfTheMinimumHeightIsRejected()
    {
        var y = Primary.Bottom - WindowPlacement.MinVisibleHeight + 1;

        var result = Resolve(Spec(300, y, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    /// <summary>
    /// 작업줄에만 가린 창. <c>MonitorFromRect</c> 는 <c>rcMonitor</c> 기준이라 이걸 통과시키는데
    /// (Stage 0 §2 단서), 판정을 <c>rcWork</c> 로 하므로 여기서 걸린다.
    /// </summary>
    [Fact]
    public void AWindowHiddenBehindTheTaskbarIsNotRecoverable()
    {
        var result = Resolve(Spec(300, Primary.Bottom + 10, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    // ── 작업영역보다 큰 창 → 축소 ────────────────────────────────────────────

    [Fact]
    public void AWindowWiderThanTheWorkAreaIsShrunkToFit()
    {
        var result = Resolve(Spec(2560, 108, 3000, 1000));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Right.Width, result.Bounds.Width);
        Assert.Equal(1000, result.Bounds.Height);
        Assert.Equal(2560, result.Bounds.X);
        Assert.Equal(108, result.Bounds.Y);
    }

    [Fact]
    public void AWindowTallerThanTheWorkAreaIsShrunkToFit()
    {
        var result = Resolve(Spec(100, 0, 1400, 4000));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Primary.Height, result.Bounds.Height);
        Assert.Equal(1400, result.Bounds.Width);
    }

    /// <summary>축소는 가장 넓게 겹친 모니터를 기준으로 한다 — 자리는 그대로 둔다.</summary>
    [Fact]
    public void ShrinkingUsesTheMonitorTheWindowMostlySitsOn()
    {
        // 왼쪽 모니터(1440 폭)에 대부분 있으면서 폭이 2000 인 창.
        var result = Resolve(Spec(-1400, 0, 2000, 800));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Left.Width, result.Bounds.Width);
        Assert.Equal(-1400, result.Bounds.X);
    }

    /// <summary>기본 위치도 작업영역보다 크면 줄인다 (§3.3-4).</summary>
    [Fact]
    public void TheDefaultSizeIsShrunkWhenTheWorkAreaIsSmaller()
    {
        ScreenRect[] small = [new(0, 0, 1024, 768)];

        var result = WindowPlacement.Resolve(Spec(-30000, -30000, 1400, 900), small, 0);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(new ScreenRect(0, 0, 1024, 768), result.Bounds);
    }

    [Fact]
    public void NoMonitorsAtAllStillProducesAUsableRectangle()
    {
        var result = WindowPlacement.Resolve(Spec(300, 200, 1400, 900), [], 0);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.True(result.Bounds.IsUsable);
    }

    [Fact]
    public void AnOutOfRangePrimaryIndexFallsBackToTheFirstMonitor()
    {
        var result = WindowPlacement.Resolve(null, Screens, primaryIndex: 7);

        Assert.Equal(Primary.X + ((Primary.Width - WindowPlacement.DefaultWidth) / 2), result.Bounds.X);
    }

    // ── 최대화 (§3.4) ────────────────────────────────────────────────────────

    [Fact]
    public void TheMaximizedFlagSurvivesRestoreUntouched()
    {
        var result = Resolve(Spec(300, 200, 1400, 900, maximized: true));

        Assert.True(result.Maximized);
        Assert.Equal(new ScreenRect(300, 200, 1400, 900), result.Bounds);
    }

    /// <summary>
    /// 방어가 발동해도 최대화 여부는 유지한다 — 좌표(복원 크기)만 기본 위치로 바뀌고,
    /// 사용자가 마지막에 두었던 "최대화" 상태는 그대로다 (§3.4).
    /// </summary>
    [Fact]
    public void TheMaximizedFlagSurvivesTheFallbackToo()
    {
        var result = Resolve(Spec(-30000, -30000, 1400, 900, maximized: true));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.True(result.Maximized);
    }

    // ── 겹침 계산 자체 ───────────────────────────────────────────────────────

    [Fact]
    public void IntersectOfDisjointRectanglesHasNoArea()
    {
        var overlap = new ScreenRect(0, 0, 100, 100).Intersect(new ScreenRect(500, 500, 100, 100));

        Assert.Equal(0, overlap.Area);
        Assert.False(overlap.IsUsable);
    }

    [Fact]
    public void CanBeGrabbedIsFalseWhenTheWindowIsNotUsable()
    {
        Assert.False(WindowPlacement.CanBeGrabbed(new ScreenRect(0, 0, 0, 0), Primary));
    }
}
