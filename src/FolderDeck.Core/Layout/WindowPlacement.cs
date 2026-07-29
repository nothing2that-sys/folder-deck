using FolderDeck.Core.Models;

namespace FolderDeck.Core.Layout;

/// <summary>화면 위의 사각형. 단위는 <b>WPF DIP</b> — 물리 픽셀은 여기까지 오지 않는다.</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>겹치는 부분. 안 겹치면 폭·높이가 0 이하인 사각형이 나온다.</summary>
    public ScreenRect Intersect(ScreenRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        return new ScreenRect(x, y, Math.Min(Right, other.Right) - x, Math.Min(Bottom, other.Bottom) - y);
    }

    public double Area => Width <= 0 || Height <= 0 ? 0 : Width * Height;

    /// <summary>폭·높이가 양수이고 좌표가 유한한가. 손으로 고친 파일이 여기로 걸린다.</summary>
    public bool IsUsable =>
        Width > 0 && Height > 0
        && double.IsFinite(X) && double.IsFinite(Y)
        && double.IsFinite(Width) && double.IsFinite(Height);
}

/// <summary>저장된 창 위치를 어떻게 처리했는가.</summary>
public enum WindowPlacementDecision
{
    /// <summary>저장된 자리·크기 그대로.</summary>
    Saved,

    /// <summary>자리는 살리고 크기만 작업영역에 맞춰 줄였다(큰 모니터 → 작은 모니터).</summary>
    Resized,

    /// <summary>되찾을 수 없는 자리라 기본 위치(주 모니터 중앙)로 보냈다.</summary>
    Fallback,
}

/// <param name="Bounds">실제로 띄울 자리 (DIP).</param>
/// <param name="Maximized">띄운 뒤 최대화할 것인가.</param>
public sealed record WindowPlacementResult(
    ScreenRect Bounds, bool Maximized, WindowPlacementDecision Decision);

/// <summary>
/// 창 위치 복원과 화면 밖 방어 (§6.1). Win32 를 부르지 않는 순수 규칙이라 Core 에 둔다 —
/// 모니터를 뽑지 않고도 경계값을 테스트할 수 있어야 한다.
/// </summary>
/// <remarks>
/// <b><c>MonitorFromRect</c> 하나로는 부족하다.</b> <c>DEFAULTTONULL</c> 은 "어느 모니터에도
/// 안 걸림"만 잡으므로 1픽셀만 걸쳐도 통과한다. 그런데 §6.1 이 막으려는 사고는 "창이 안 보이는
/// 곳에 떠 못 찾는 것"이고, 1픽셀 걸친 창은 사실상 못 찾는다. 그래서 판정을 두 단계로 둔다 —
/// 작업영역과 겹치는가, 그리고 <b>겹치는 부분으로 창을 되찾을 수 있는가</b>.
/// <para>
/// 좌표 부호로 판정하지 않는다. 이 PC 의 <c>DISPLAY3</c> 은 정상 위치가 (-1440, -271) 이라
/// <c>x &lt; 0</c> 같은 검사는 멀쩡한 창을 화면 밖으로 오판한다(Stage 0 §1 의 실측 반례).
/// </para>
/// </remarks>
public static class WindowPlacement
{
    /// <summary>
    /// 창을 되찾으려면 작업영역과 이만큼은 겹쳐야 한다 (DIP).
    /// </summary>
    /// <remarks>
    /// "보이기만 한다"가 아니라 <b>마우스로 잡아 끌 수 있다</b>를 기준으로 잡았고, 수치는
    /// 이 PC 에서 잰 창 테두리 크기에서 나왔다(Windows 11 · 100%):
    /// <c>WindowCaptionHeight</c> 23 · <c>ResizeFrameHorizontalBorderHeight</c> 4 ·
    /// <c>WindowCaptionButtonWidth</c> 36(버튼 3개 = 108).
    /// <list type="bullet">
    /// <item>가로 160 = 캡션 버튼 108 + 끌 수 있는 제목줄 약 52. 버튼만 보이면 닫을 수는
    /// 있어도 끌 자리가 없어 "되찾는다"가 안 된다.</item>
    /// <item>세로 32 = 제목줄 23 + 위쪽 테두리 4 에 여유. 제목줄 전체가 보인다는 뜻이다.</item>
    /// </list>
    /// 값을 코드 곳곳에 흩지 않고 여기 하나로 둔다 — 판정과 테스트가 같은 수를 본다.
    /// </remarks>
    public const double MinVisibleWidth = 160;

    /// <inheritdoc cref="MinVisibleWidth"/>
    public const double MinVisibleHeight = 32;

    /// <summary>기본 크기 (§7.1 예시). 작업영역이 더 작으면 거기에 맞춰 줄인다.</summary>
    public const double DefaultWidth = 1400;

    /// <inheritdoc cref="DefaultWidth"/>
    public const double DefaultHeight = 900;

    /// <summary>
    /// 저장된 위치를 지금 화면 구성에 맞춰 실제로 띄울 자리로 바꾼다.
    /// </summary>
    /// <param name="saved">워크스페이스에 저장된 값. null 이면 기본 위치.</param>
    /// <param name="workAreas">현재 모니터들의 작업영역 (DIP). 작업줄을 뺀 값이어야 한다.</param>
    /// <param name="primaryIndex">주 모니터의 <paramref name="workAreas"/> 안 위치.</param>
    public static WindowPlacementResult Resolve(
        WindowSpec? saved, IReadOnlyList<ScreenRect> workAreas, int primaryIndex)
    {
        ArgumentNullException.ThrowIfNull(workAreas);

        var maximized = saved?.Maximized ?? false;

        if (saved is null)
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        var wanted = new ScreenRect(saved.X, saved.Y, saved.Width, saved.Height);
        if (!wanted.IsUsable)
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        // ① 어느 작업영역과도 안 겹치면 그대로 실패다. 모니터를 뺀 경우가 여기다.
        var host = MostOverlapping(wanted, workAreas);
        if (host is not { } work)
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        // ② 작업영역보다 큰 창은 먼저 줄인다 — 큰 모니터에서 작은 모니터로 바뀐 경우.
        //    줄인 뒤에 되찾을 수 있는지를 보는 이유는, 실제로 띄울 사각형이 이쪽이기 때문이다.
        var fitted = wanted with
        {
            Width = Math.Min(wanted.Width, work.Width),
            Height = Math.Min(wanted.Height, work.Height),
        };

        // ③ 되찾을 수 있는가. 가장 많이 겹치는 모니터가 아니어도 되므로 전부 본다 —
        //    두 모니터에 걸친 창은 어느 한쪽에서 제목줄을 잡을 수 있으면 그만이다.
        if (!workAreas.Any(area => CanBeGrabbed(fitted, area)))
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        return new WindowPlacementResult(
            fitted,
            maximized,
            fitted == wanted ? WindowPlacementDecision.Saved : WindowPlacementDecision.Resized);
    }

    /// <summary>
    /// 이 작업영역만으로 창을 되찾을 수 있는가 — 겹치는 부분이 충분히 크고, 거기에
    /// <b>창의 위쪽 변</b>(제목줄이 있는 쪽)이 들어 있는가.
    /// </summary>
    /// <remarks>
    /// 제목줄을 잡을 수 없으면 아무리 넓게 겹쳐도 못 찾는 것과 같다. 아래쪽만 걸친 창
    /// (제목줄이 작업영역 위로 넘어간 창)이 정확히 그 경우다.
    /// </remarks>
    public static bool CanBeGrabbed(ScreenRect window, ScreenRect workArea)
    {
        if (!window.IsUsable)
        {
            return false;
        }

        var overlap = window.Intersect(workArea);
        return overlap.Width >= MinVisibleWidth
               && overlap.Height >= MinVisibleHeight
               && window.Y >= workArea.Y;
    }

    /// <summary>가장 넓게 겹치는 작업영역. 하나도 안 겹치면 null.</summary>
    private static ScreenRect? MostOverlapping(ScreenRect window, IReadOnlyList<ScreenRect> workAreas)
    {
        ScreenRect? best = null;
        var bestArea = 0d;

        foreach (var area in workAreas)
        {
            var overlap = window.Intersect(area).Area;
            if (overlap > bestArea)
            {
                bestArea = overlap;
                best = area;
            }
        }

        return best;
    }

    /// <summary>기본 위치 — 주 모니터 중앙에 기본 크기. 작업영역이 더 작으면 거기 맞춘다.</summary>
    private static WindowPlacementResult CenterOnPrimary(
        IReadOnlyList<ScreenRect> workAreas, int primaryIndex, bool maximized)
    {
        // 작업영역을 하나도 못 얻은 경우(모니터 열거 실패)까지 여기서 받는다.
        var work = workAreas.Count == 0
            ? new ScreenRect(0, 0, DefaultWidth, DefaultHeight)
            : workAreas[primaryIndex >= 0 && primaryIndex < workAreas.Count ? primaryIndex : 0];

        var width = Math.Min(DefaultWidth, work.Width);
        var height = Math.Min(DefaultHeight, work.Height);

        return new WindowPlacementResult(
            new ScreenRect(
                work.X + ((work.Width - width) / 2),
                work.Y + ((work.Height - height) / 2),
                width,
                height),
            maximized,
            WindowPlacementDecision.Fallback);
    }
}
