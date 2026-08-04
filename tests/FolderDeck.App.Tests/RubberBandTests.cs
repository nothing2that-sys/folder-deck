using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

/// <summary>
/// 고무줄 선택 (§4.2-2 · Phase 5 Stage 3B).
/// </summary>
/// <remarks>
/// 여기서 시험하는 것은 <b>판정과 기하</b>다 — 실제 마우스 제스처와 캡처는 뷰의 몫이라 흉내
/// 내지 않는다(<see cref="ShellDropTests"/> · <see cref="TileDropTests"/> 와 같은 경계).
/// 그래서 갈림을 죄다 <see cref="RubberBand"/> 의 순수 함수로 빼 두었고, 뷰에 남은 것은
/// 붙잡고 있는 상태뿐이다.
/// <para>
/// 조상 사슬은 <b>타입 목록</b>으로 만든다. WPF 컨트롤은 <b>만드는 것만으로도 STA 스레드를
/// 요구하고</b>(실측: `InputManager..ctor` 에서 `호출 스레드가 STA 여야 합니다`) 이 테스트
/// 어셈블리는 MTA 로 돈다 — 그래서 <see cref="RubberBand.Classify"/> 가 요소가 아니라 타입을
/// 받는다. 시각 트리를 실제로 훑는 <see cref="RubberBand.AncestorTypes"/> 는 요소가 필요해
/// 여기서 덮지 못한다(손 확인 몫이다).
/// </para>
/// </remarks>
public sealed class RubberBandTests
{
    // ── 눌린 자리 판정 (§4-2) ────────────────────────────────────────────────

    /// <summary>아무것도 안 걸리면 빈 곳이다 — 고무줄이 시작된다.</summary>
    [Fact]
    public void PressingSomethingThatIsNeitherAnItemNorAScrollBarIsEmptySpace() =>
        Assert.Equal(
            ListHitKind.Empty,
            RubberBand.Classify(
                [typeof(Border), typeof(ScrollContentPresenter), typeof(ScrollViewer), typeof(ListView)]));

    [Fact]
    public void AnEmptyAncestorChainIsEmptySpace() =>
        Assert.Equal(ListHitKind.Empty, RubberBand.Classify([]));

    /// <summary>
    /// <b><see cref="ListViewItem"/> 조상이 있으면 행 어디든 항목이다</b> — 2026-08-04 결정.
    /// <c>GridView</c> 상세는 행을 가로 전체로 늘리므로 <b>파일 이름 오른쪽 빈 자리</b>를 눌러도
    /// 히트테스트가 행을 준다. 셀 안 좌표를 계산하지 않는다(§3).
    /// </summary>
    [Fact]
    public void AnythingInsideARowIsAnItemEvenTheEmptySpaceToTheRightOfTheName()
    {
        // 이름 오른쪽 빈 자리를 누른 모양 — 행 안의 빈 칸이 원본이다.
        Assert.Equal(
            ListHitKind.Item,
            RubberBand.Classify(
                [typeof(Border), typeof(GridViewRowPresenter), typeof(ListViewItem), typeof(ListView)]));

        // 이름 글자를 직접 누른 모양.
        Assert.Equal(
            ListHitKind.Item,
            RubberBand.Classify([typeof(TextBlock), typeof(ListViewItem), typeof(ListView)]));
    }

    /// <summary>파생 타입도 잡는다 — <c>is</c> 와 같게 본다.</summary>
    [Fact]
    public void ASubclassOfARowIsStillARow() =>
        Assert.Equal(ListHitKind.Item, RubberBand.Classify([typeof(DerivedRow)]));

    private sealed class DerivedRow : ListViewItem;

    /// <summary>
    /// <b>스크롤바를 먼저 본다.</b> 썸은 <see cref="ListView"/> 템플릿 안에 있어, 순서를 뒤집으면
    /// 끌어야 할 스크롤이 고무줄로 먹힌다(§4-2 · 손 확인 17).
    /// </summary>
    [Fact]
    public void PressingTheScrollBarIsNeitherRubberBandNorDrag()
    {
        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Thumb), typeof(Track), typeof(ScrollBar)]));
        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Border), typeof(ScrollBar)]));

        // 썸만 있고 ScrollBar 가 사슬에 안 잡히는 모양도 막는다.
        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Thumb)]));
    }

    /// <summary>
    /// 사슬에 <b>둘 다</b> 있으면 스크롤바가 이긴다 — "누가 더 가까운가"가 답을 정하게 두지 않는다.
    /// </summary>
    [Fact]
    public void TheScrollBarWinsEvenWhenAnItemIsCloserInTheChain() =>
        Assert.Equal(
            ListHitKind.None,
            RubberBand.Classify([typeof(ListViewItem), typeof(ScrollBar), typeof(ListView)]));

    // ── 조상 사슬 뽑기 — 요소가 아닌 입력만 (나머지는 STA 가 필요하다) ───────

    [Fact]
    public void NoSourceMeansNoChain()
    {
        Assert.Empty(RubberBand.AncestorTypes(null, null));
        Assert.Empty(RubberBand.AncestorTypes("요소가 아니다", null));

        // 사슬이 비면 빈 곳이다 — 판정과 이어 붙여도 예외가 없다.
        Assert.Equal(ListHitKind.Empty, RubberBand.Classify(RubberBand.AncestorTypes(null, null)));
    }

    // ── 교차 판정 경계 (§3 "겹치는 항목을 고른다") ───────────────────────────

    private static readonly Rect Row = new(0, 20, 200, 20);   // x 0~200 · y 20~40

    [Fact]
    public void ARowThatOverlapsTheBandIsPicked()
    {
        // 완전히 감싸지 않아도 고른다 — 아래쪽 몇 픽셀만 스쳐도 된다.
        Assert.True(RubberBand.Touches(new Rect(0, 0, 10, 21), Row));

        // 통째로 감싼 경우.
        Assert.True(RubberBand.Touches(new Rect(0, 0, 300, 100), Row));

        // 사각형이 행 안에 완전히 들어간 경우도 겹침이다.
        Assert.True(RubberBand.Touches(new Rect(50, 25, 10, 5), Row));
    }

    /// <summary>
    /// <b>맞닿은 것은 겹침이 아니다</b> — 겹친 면적이 0 이면 겹친 것이 아니다.
    /// <c>Rect.IntersectsWith</c> 를 쓰지 않은 이유가 이것이다(그것은 맞닿은 것도 참으로 준다).
    /// <b>이 단계가 정한 것이고 탐색기를 실측한 것이 아니다.</b>
    /// </summary>
    [Fact]
    public void ARowThatOnlyTouchesTheBandEdgeIsNotPicked()
    {
        // 사각형의 아래 변이 행의 위 변에 딱 맞닿았다 (y 0~20 · 행은 20 부터).
        Assert.False(RubberBand.Touches(new Rect(0, 0, 200, 20), Row));
        Assert.True(RubberBand.Touches(new Rect(0, 0, 200, 20.5), Row));

        // 왼/오른쪽도 같다.
        Assert.False(RubberBand.Touches(new Rect(200, 20, 50, 20), Row));
        Assert.True(RubberBand.Touches(new Rect(199.5, 20, 50, 20), Row));
    }

    [Fact]
    public void ARowThatMissesTheBandEntirelyIsNotPicked()
    {
        Assert.False(RubberBand.Touches(new Rect(0, 0, 200, 10), Row));   // 위로 비껴갔다
        Assert.False(RubberBand.Touches(new Rect(0, 60, 200, 10), Row));  // 아래로
        Assert.False(RubberBand.Touches(new Rect(300, 20, 50, 20), Row)); // 옆으로
    }

    /// <summary>
    /// <b>세로로만 끈 사각형(너비 0)도 지나간 행을 고른다.</b> 맞닿기 배제와 어긋나 보이지만
    /// 같은 규칙이다: 맞닿은 사각형은 행 <b>밖</b>에 있고(경계만 공유), 너비 0 인 선은 행
    /// <b>안</b>을 지난다. 이것이 아니면 똑바로 아래로 끄는 흔한 동작이 아무것도 못 고른다.
    /// </summary>
    [Fact]
    public void AZeroWidthBandStillPicksTheRowsItPassesThrough()
    {
        Assert.True(RubberBand.Touches(new Rect(100, 0, 0, 100), Row));

        // 행의 x 범위 밖을 지나는 선은 못 고른다 — 안을 지나는가가 답을 정한다.
        Assert.False(RubberBand.Touches(new Rect(250, 0, 0, 100), Row));

        // 행의 왼쪽 변에 딱 붙은 선은 안이 아니라 경계다.
        Assert.False(RubberBand.Touches(new Rect(0, 0, 0, 100), Row));
    }

    /// <summary>어느 방향으로 끌어도 같은 사각형이 된다.</summary>
    [Fact]
    public void DraggingBackwardsMakesTheSameRectangle()
    {
        var down = RubberBand.Between(new Point(10, 10), new Point(50, 40));
        var up = RubberBand.Between(new Point(50, 40), new Point(10, 10));

        Assert.Equal(down, up);
        Assert.Equal(new Rect(10, 10, 40, 30), down);
    }

    // ── 임계 (§4-6 — 미달이면 고무줄이 아니라 클릭) ──────────────────────────

    /// <summary>
    /// 안 움직였거나 몇 픽셀만 움직인 것은 <b>클릭이다.</b> 그때는 빈 곳 클릭이 지금 하는 일이
    /// 그대로 남아야 한다 — §2 가 "앱 코드가 아무것도 하지 않는다"고 확인한 자리다.
    /// </summary>
    [Fact]
    public void NotMovingFarEnoughIsAClickNotARubberBand()
    {
        var origin = new Point(100, 100);

        Assert.False(RubberBand.PastThreshold(origin, origin));
        Assert.False(RubberBand.PastThreshold(origin, new Point(100.5, 100.5)));
    }

    /// <summary>어느 한 축이라도 임계를 넘으면 고무줄이다 — 기존 항목 드래그와 같은 규칙이다.</summary>
    [Fact]
    public void CrossingTheThresholdOnEitherAxisStartsTheRubberBand()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(RubberBand.PastThreshold(origin, new Point(100 + h, 100)));
        Assert.True(RubberBand.PastThreshold(origin, new Point(100, 100 + v)));

        // 방향은 상관없다.
        Assert.True(RubberBand.PastThreshold(origin, new Point(100 - h, 100)));
        Assert.True(RubberBand.PastThreshold(origin, new Point(100, 100 - v)));

        // 바로 아래는 아직 클릭이다.
        Assert.False(RubberBand.PastThreshold(origin, new Point(100 + h - 0.01, 100 + v - 0.01)));
    }

    // ── 시작하는 순간의 수정키 (§3) ──────────────────────────────────────────

    /// <summary><c>Ctrl</c> 없이 시작하면 기존 선택을 <b>지운다</b> — 탐색기와 같다.</summary>
    [Fact]
    public void StartingWithoutControlThrowsAwayTheOldSelection() =>
        Assert.Empty(RubberBand.Kept(adds: false, ["가", "나"]));

    /// <summary><c>Ctrl</c> 로 시작하면 기존 선택을 <b>지킨다</b> — 탐색기 관용.</summary>
    [Fact]
    public void StartingWithControlKeepsTheOldSelection() =>
        Assert.Equal(["가", "나"], RubberBand.Kept(adds: true, ["가", "나"]));

    /// <summary>지켜 둔 것은 <b>복사본</b>이다 — 뒤이어 선택을 비워도 남아 있어야 한다.</summary>
    [Fact]
    public void TheKeptSelectionIsASnapshotNotALiveView()
    {
        var live = new List<object> { "가", "나" };
        var kept = RubberBand.Kept(adds: true, live);

        live.Clear();

        Assert.Equal(2, kept.Count);
    }

    // ── 고른 것 = 지킨 것 + 겹친 것 ──────────────────────────────────────────

    private static (object Item, Rect Bounds) RowAt(string name, double top) =>
        (name, new Rect(0, top, 200, 20));

    /// <summary>사각형이 훑은 것들이 골라진다.</summary>
    [Fact]
    public void TheBandPicksTheRowsItOverlaps()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted([], rows, new Rect(0, 10, 100, 20));

        Assert.Equal<object>(["가", "나"], picked.OrderBy(o => (string)o));
    }

    /// <summary>
    /// <b><c>Ctrl</c> 더하기</b> — 지켜 둔 것은 사각형이 안 스쳐도 남는다. 사각형을 다시 줄여도
    /// 빠지지 않아야 한다(그 자리가 이 함수의 존재 이유다).
    /// </summary>
    [Fact]
    public void ControlAddsToTheSelectionInsteadOfReplacingIt()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted(["다"], rows, new Rect(0, 0, 100, 5));

        Assert.Equal<object>(["가", "다"], picked.OrderBy(o => (string)o));
    }

    /// <summary><c>Ctrl</c> 없이 시작한 뒤에는 사각형이 훑은 것만 남는다.</summary>
    [Fact]
    public void WithoutControlOnlyWhatTheBandOverlapsSurvives()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted(
            RubberBand.Kept(adds: false, ["다"]), rows, new Rect(0, 0, 100, 5));

        Assert.Equal<object>(["가"], picked);
    }

    /// <summary>같은 항목이 두 경로로 들어와도 한 번만 골라진다.</summary>
    [Fact]
    public void AnItemThatIsBothKeptAndOverlappedIsPickedOnce()
    {
        var picked = RubberBand.Wanted(["가"], [RowAt("가", 0)], new Rect(0, 0, 100, 100));

        Assert.Single(picked);
    }

    /// <summary>
    /// <b>빈 목록</b>에서도 예외 없이 아무것도 안 골라진다 — 아직 안 띄운 칸·검색 결과 없음이
    /// 이 자리다.
    /// </summary>
    [Fact]
    public void AnEmptyListPicksNothingAndDoesNotThrow() =>
        Assert.Empty(RubberBand.Wanted([], [], new Rect(0, 0, 500, 500)));

    /// <summary>
    /// 화면 밖 항목은 아예 들어오지 않는다 — 가상화가 켜져 있어 사각형이 없다. 큰 사각형을 그려도
    /// 실현된 것만 골라진다(자동 스크롤이 v1 제외라 이것이 일관된다 · 손 확인 19).
    /// </summary>
    [Fact]
    public void OnlyRealizedRowsCanBePicked()
    {
        // 목록이 열 개여도 화면에 둘만 있으면 넘어오는 것이 둘이다.
        var onScreen = new[] { RowAt("가", 0), RowAt("나", 20) };

        var picked = RubberBand.Wanted([], onScreen, new Rect(0, 0, 500, 5000));

        Assert.Equal(2, picked.Count);
    }
}
