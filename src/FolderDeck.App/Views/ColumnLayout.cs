using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FolderDeck.App.Views;

/// <summary>
/// 상세 뷰(<c>GridView</c>) 컬럼의 <b>자동 폭</b> (§3.2 · Phase 5 Stage 3D-2).
/// </summary>
/// <remarks>
/// <b><c>GridView</c> 는 star 폭을 지원하지 않는다</b>(<c>Auto</c> 도 내용에 맞추는 것이라 남는
/// 자리를 먹지 않는다). 그래서 컬럼 하나를 코드로 계산해 넣는다 — 어느 컬럼인지는 XAML 에서
/// <see cref="IsFlexibleProperty"/> 로 표시한다.
/// <para>
/// 계산은 <see cref="FlexWidth"/> 하나뿐이고 <b>상태도 WPF 요소도 만들지 않는다</b> — 뷰 밖에서
/// 시험할 수 있게 <see cref="RubberBand"/> 와 같은 결로 뺐다(테스트 어셈블리는 MTA 라 WPF 컨트롤을
/// 만들 수 없다). 언제 다시 재는지와 재진입 차단은 <see cref="FolderPanelView"/> 것이다.
/// </para>
/// </remarks>
public static class ColumnLayout
{
    /// <summary>
    /// 늘린 컬럼 오른쪽에 <b>남기는 죽은 자리</b> — 고무줄을 시작하는 자리다 (§4.2-2 · Stage 3C).
    /// </summary>
    /// <remarks>
    /// <b>꽉 맞추면 단계 C 가 되돌아간다.</b> 단계 C 가 행의 히트 영역을 내용 폭까지 좁혔으므로
    /// (`FileItemContainer` 의 `HorizontalAlignment="Left"`) 컬럼이 뷰포트를 다 먹으면 세로로 꽉 찬
    /// 목록에서 고무줄을 시작할 빈 곳이 하나도 없다. 탐색기 상세 뷰도 오른쪽에 죽은 자리를 남긴다.
    /// <para>
    /// <b>재서 얻은 최적값이 아니라 정한 값이다.</b> 실측이 말해 준 것은 "행 오른쪽 4px 부터 이미
    /// 빈 곳으로 판정된다"는 것뿐이다 — 판정에 필요한 폭은 그보다 훨씬 작다. 32px 는 <b>손으로
    /// 집을 수 있는 폭</b>으로 고른 것이고, 좁아서 못 집겠으면 이 숫자만 키우면 된다.
    /// </para>
    /// </remarks>
    public const double RightMargin = 32;

    /// <summary>
    /// 늘리는 컬럼의 <b>최소 폭</b>. 이 아래로는 줄이지 않고 가로 스크롤에 맡긴다.
    /// </summary>
    /// <remarks>
    /// <b>정한 값이다.</b> 원래 폭 260 의 60% 남짓이고, 글리프를 빼고도 파일 이름 스무 자쯤이
    /// 보인다 — 그보다 좁히면 이름이 다 잘려서 늘린 뜻이 없어진다. 1x1 타일은 어차피 고정 컬럼
    /// 합(220)조차 못 담아 가로 스크롤이다.
    /// </remarks>
    public const double MinFlexWidth = 160;

    /// <summary>
    /// 행이 컬럼 합 위에 <b>더 먹는 폭</b>(테두리·여백).
    /// </summary>
    /// <remarks>
    /// <b>실측 6px</b>(격리 스파이크 · 2026-08-05): 컬럼 합 480 → 행 <c>ActualWidth</c> 486,
    /// 컬럼 합 389 → 행 395. 단계 C 가 잰 "컬럼 합 180 + 6 = 186" 과 같은 값이다.
    /// 이것을 빼지 않으면 <see cref="RightMargin"/> 이 실제로는 6px 좁아진다.
    /// </remarks>
    public const double RowChrome = 6;

    /// <summary>
    /// 이 컬럼이 <b>남는 폭을 먹는다</b> — 상세 뷰의 `이름`, 검색 뷰의 `위치`.
    /// </summary>
    /// <remarks>
    /// 인덱스나 머리글 글자로 찾지 않는다. 두 <c>GridView</c> 가 서로 다른 자리의 컬럼을 늘리므로
    /// (§3.2 규칙) XAML 에 표시해 두는 것이 그 사실이 보이는 유일한 자리다.
    /// </remarks>
    public static readonly DependencyProperty IsFlexibleProperty =
        DependencyProperty.RegisterAttached(
            "IsFlexible", typeof(bool), typeof(ColumnLayout), new PropertyMetadata(false));

    public static void SetIsFlexible(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsFlexibleProperty, value);
    }

    public static bool GetIsFlexible(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (bool)target.GetValue(IsFlexibleProperty);
    }

    /// <summary>표시된 컬럼. 없으면 null — 그러면 아무것도 건드리지 않는다.</summary>
    public static GridViewColumn? FlexColumn(GridView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Columns.FirstOrDefault(GetIsFlexible);
    }

    /// <summary>
    /// 늘리는 컬럼의 폭 — <b>뷰포트에서 나머지와 여백을 뺀 것</b>, 최소 폭에서 멈춘다.
    /// </summary>
    /// <remarks>
    /// <paramref name="viewportWidth"/> 는 <c>ActualWidth</c> 가 아니라 <b>세로 스크롤바를 뺀
    /// 콘텐츠 폭</b>이어야 한다 — <c>ScrollViewer.ViewportWidth</c> 가 그 값이다(실측: 폭 444
    /// 목록에서 스크롤바가 있으면 427, 없으면 444). <c>ActualWidth</c> 를 쓰면 스크롤바가 뜬 만큼
    /// 컬럼이 넘쳐 가로 스크롤이 생기고, 그것이 §3-3 의 진동 고리를 연다.
    /// <para>
    /// <see cref="RowChrome"/> 도 뺀다 — 행은 컬럼 합보다 6px 넓게 그려지므로 그것을 빼지 않으면
    /// 남는 여백이 <paramref name="rightMargin"/> 보다 좁아진다.
    /// </para>
    /// <para>
    /// <b>정수로 내린다.</b> 소수점이 남으면 레이아웃마다 값이 미세하게 흔들려 재진입 차단이
    /// 걸리지 않을 수 있다. 아직 레이아웃 전(폭이 0 이나 NaN)이면 최소 폭을 낸다 —
    /// <b>그 값을 쓸지는 부르는 쪽이 정한다</b>(뷰는 폭이 잡히기 전에는 쓰지 않는다).
    /// </para>
    /// <para>
    /// 셈이 유한하지 않으면(어느 컬럼이 <c>Auto</c> = <c>NaN</c> 폭이면 합이 <c>NaN</c> 이다)
    /// 최소 폭으로 떨어진다. <b>지금 컬럼은 전부 고정 픽셀이라 그 자리에 닿지 않지만</b>,
    /// <c>NaN</c> 을 그대로 흘리면 늘리려던 컬럼이 조용히 <c>Auto</c> 가 된다.
    /// </para>
    /// </remarks>
    public static double FlexWidth(
        double viewportWidth, double otherColumnsWidth, double rightMargin, double minWidth)
    {
        if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
        {
            return minWidth;
        }

        var available = Math.Floor(viewportWidth - otherColumnsWidth - rightMargin - RowChrome);
        return double.IsFinite(available) ? Math.Max(minWidth, available) : minWidth;
    }
}
