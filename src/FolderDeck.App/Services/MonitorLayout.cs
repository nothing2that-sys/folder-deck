using System.Runtime.InteropServices;
using FolderDeck.Core.Layout;

namespace FolderDeck.App.Services;

public static class MonitorLayout
{

    public sealed record Snapshot(IReadOnlyList<ScreenRect> WorkAreas, int PrimaryIndex, double Scale);

    public static Snapshot Query()
    {
        var scale = GetDpiForSystem() / 96.0;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1.0;
        }

        var areas = new List<ScreenRect>();
        var primaryIndex = 0;

        bool Collect(IntPtr monitor, IntPtr hdc, ref Rect clip, IntPtr data)
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>(), szDevice = string.Empty };
            if (!GetMonitorInfoW(monitor, ref info))
            {
                return true;
            }

            if ((info.dwFlags & MonitorPrimary) != 0)
            {
                primaryIndex = areas.Count;
            }

            var work = info.rcWork;
            areas.Add(new ScreenRect(
                work.Left / scale,
                work.Top / scale,
                (work.Right - work.Left) / scale,
                (work.Bottom - work.Top) / scale));

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Collect, IntPtr.Zero);

        return new Snapshot(areas, primaryIndex, scale);
    }

    private const uint MonitorPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect clip, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
