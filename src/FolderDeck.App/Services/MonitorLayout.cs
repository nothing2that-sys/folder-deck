using System.Runtime.InteropServices;
using FolderDeck.Core.Layout;

namespace FolderDeck.App.Services;

/// <summary>
/// 지금 붙어 있는 모니터들의 작업영역 (§6.1). Win32 호출이라 플랫폼 중립인 Core 밖에 둔다
/// (<see cref="RecycleBin"/> 와 같은 자리·같은 모양).
/// </summary>
/// <remarks>
/// WPF <c>SystemParameters.WorkArea</c> 는 <b>쓰지 않는다</b> — 창을 어느 모니터에 두고 재도
/// 주 모니터의 작업영역만 준다(Stage 0 §2 실측). <c>VirtualScreen*</c> 도 아니다. 모니터
/// 사이 빈 공간까지 포함하는 큰 사각형 하나라 "화면 안인가" 판정에 쓰면 틀린다.
/// <para>
/// <c>Screen.AllScreens</c>(WinForms)는 값이 이것과 완전히 같아서(같은 API 를 부른다)
/// 어셈블리를 하나 더 끌어들일 이유가 없었다.
/// </para>
/// </remarks>
public static class MonitorLayout
{
    /// <param name="WorkAreas">작업줄을 뺀 작업영역들 (DIP).</param>
    /// <param name="PrimaryIndex">주 모니터의 <paramref name="WorkAreas"/> 안 위치. 못 찾으면 0.</param>
    /// <param name="Scale">물리 픽셀 → DIP 변환에 쓴 계수 (시스템 DPI / 96).</param>
    public sealed record Snapshot(IReadOnlyList<ScreenRect> WorkAreas, int PrimaryIndex, double Scale);

    /// <summary>
    /// 지금 구성을 읽는다. <b>변환은 여기 한 번뿐이다</b> — 픽셀 값은 이 메서드 밖으로
    /// 나가지 않는다.
    /// </summary>
    /// <remarks>
    /// 이 앱은 매니페스트로 System DPI 인식이라 DIP↔픽셀 계수가 <b>프로세스에 하나</b>다
    /// (모니터마다 갈리지 않는다). 그래서 <c>GetDpiForSystem() / 96</c> 하나로 나눈다.
    /// 창이 생기기 전에 불러도 같은 값이라는 것이 매니페스트를 넣은 이유다(§3.1).
    /// <para>
    /// <b>여기서 받는 <c>rcWork</c> 는 "물리 픽셀"이 아니다.</b> 배율이 섞인 환경에서 실측한
    /// 결과, 시스템 인식 프로세스는 시스템 DPI 를 기준으로 <b>가상화된</b> 값을 받는다 —
    /// 125% 로 올린 1080x1920 모니터를 PerMonitorV2 프로세스는 그대로 보지만 이 앱은
    /// 864x1536(= 1080/1.25) 으로 본다(원점은 그대로, 크기만 나뉜다). 그래서 모니터마다
    /// 계수를 따로 둘 필요가 없고, 위 나눗셈 하나가 맞는다. 배율이 다른 모니터가 있어도
    /// 이 프로세스 안에서는 좌표계가 하나다.
    /// </para>
    /// PerMonitorV2 로 바꾸면 이 가정이 깨지므로 그때는 이 메서드부터 다시 봐야 한다.
    /// </remarks>
    public static Snapshot Query()
    {
        var scale = GetDpiForSystem() / 96.0;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1.0;
        }

        var areas = new List<ScreenRect>();
        var primaryIndex = 0;

        // 콜백은 EnumDisplayMonitors 가 돌아오기 전에만 불리므로 지역 델리게이트로 충분하다.
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

    // szDevice 가 고정 길이 문자 배열이라 소스 생성 P/Invoke(LibraryImport)로는 마샬링되지
    // 않는다. RecycleBin 의 SHFileOperationW 와 같은 사정이고 같은 방식으로 둔다.
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
