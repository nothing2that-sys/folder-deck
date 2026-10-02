using FolderDeck.Core.Models;

namespace FolderDeck.Core.Layout;

public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public ScreenRect Intersect(ScreenRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        return new ScreenRect(x, y, Math.Min(Right, other.Right) - x, Math.Min(Bottom, other.Bottom) - y);
    }

    public double Area => Width <= 0 || Height <= 0 ? 0 : Width * Height;

    public bool IsUsable =>
        Width > 0 && Height > 0
        && double.IsFinite(X) && double.IsFinite(Y)
        && double.IsFinite(Width) && double.IsFinite(Height);
}

public enum WindowPlacementDecision
{

    Saved,

    Resized,

    Fallback,

    MinimumEnforced,
}

public sealed record WindowPlacementResult(
    ScreenRect Bounds, bool Maximized, WindowPlacementDecision Decision);

public static class WindowPlacement
{

    public const double MinVisibleWidth = 160;

    public const double MinVisibleHeight = 32;

    public const double DefaultWidth = 1400;

    public const double DefaultHeight = 900;

    public const double MinWindowWidth = 640;

    public const double MinWindowHeight = 480;

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

        var host = MostOverlapping(wanted, workAreas);
        if (host is not { } work)
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        var capped = wanted with
        {
            Width = Math.Min(wanted.Width, work.Width),
            Height = Math.Min(wanted.Height, work.Height),
        };

        var fitted = capped with
        {
            Width = Math.Max(capped.Width, MinWindowWidth),
            Height = Math.Max(capped.Height, MinWindowHeight),
        };

        if (!workAreas.Any(area => CanBeGrabbed(fitted, area)))
        {
            return CenterOnPrimary(workAreas, primaryIndex, maximized);
        }

        var decision = fitted != capped
            ? WindowPlacementDecision.MinimumEnforced
            : fitted == wanted
                ? WindowPlacementDecision.Saved
                : WindowPlacementDecision.Resized;

        return new WindowPlacementResult(fitted, maximized, decision);
    }

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

    private static WindowPlacementResult CenterOnPrimary(
        IReadOnlyList<ScreenRect> workAreas, int primaryIndex, bool maximized)
    {

        var work = workAreas.Count == 0
            ? new ScreenRect(0, 0, DefaultWidth, DefaultHeight)
            : workAreas[primaryIndex >= 0 && primaryIndex < workAreas.Count ? primaryIndex : 0];

        var width = Math.Max(Math.Min(DefaultWidth, work.Width), MinWindowWidth);
        var height = Math.Max(Math.Min(DefaultHeight, work.Height), MinWindowHeight);

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
