using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FolderDeck.App.Services;

/// <summary>
/// 자기 창을 앞으로 가져온다 (§3.3). 신호를 받은 프로세스가 UI 스레드에서 부른다.
/// </summary>
/// <remarks>
/// Stage 0 실측 조합의 받는 쪽 절반이다 — 보내는 쪽이
/// <c>AllowSetForegroundWindow(ASFW_ANY)</c> 로 권한을 넘기고, 여기서 복원 후 포그라운드로
/// 올린다. 최소화된 창도 복원되는 것을 두 프로세스로 확인했다.
/// <para>
/// <b><c>SW_RESTORE</c> 는 최소화 상태에서만 부른다.</b> 최대화된 창에 그냥 부르면 최대화가
/// 풀려 창 크기가 바뀐다 — 사용자는 앞으로 오라고만 했다.
/// </para>
/// </remarks>
public static class WindowActivation
{
    private const int SwRestore = 9;

    /// <summary>최소화되어 있으면 복원하고, 포그라운드로 올린다.</summary>
    public static void BringToFront(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            // 아직 창이 만들어지지 않았다(신호가 시작 도중에 왔다). Show() 가 곧 앞으로 띄운다.
            return;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SwRestore);
        }

        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
