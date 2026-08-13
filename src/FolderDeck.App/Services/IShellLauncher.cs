using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FolderDeck.App.Services;

/// <summary>
/// 셸에 넘기는 동작 (§4.5 · §4.6). COM 셸 컨텍스트 메뉴가 아니라 단순 실행 수준이다.
/// 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface IShellLauncher
{
    /// <summary>OS 기본 프로그램으로 열기. 성공하면 null, 실패하면 사용자에게 보일 메시지.</summary>
    string? Open(string path);

    /// <summary>
    /// 탐색기에서 열기 — 폴더면 그 폴더를, 파일이면 그 파일이 든 폴더를 열고 파일을 선택한다.
    /// </summary>
    string? RevealInExplorer(string path, bool isDirectory);

    /// <summary>
    /// 셸 속성 대화상자를 띄운다 (§4.6). 성공하면 null, 실패하면 사용자에게 보일 메시지.
    /// </summary>
    /// <remarks>
    /// <see cref="Open"/> 과 같은 층이다 — <b>상태를 바꾸지 않는 읽기</b>이고, 창을 띄우는 것은
    /// 셸이다. 앱이 속성을 그리지 않는다.
    /// </remarks>
    string? ShowProperties(string path);
}

/// <inheritdoc/>
public sealed class ShellLauncher : IShellLauncher
{
    public string? Open(string path)
    {
        try
        {
            // UseShellExecute = true 라야 확장자 연결이 적용된다(실행이 아니라 "열기").
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <remarks>
    /// 호출 형태는 실측으로 정했다(§11 — 기억으로 단정하지 말 것):
    /// <list type="bullet">
    /// <item><c>ArgumentList</c> 는 <c>/select,</c> 를 깨뜨린다 — 인자별로 다시 인용되면서
    /// explorer 가 경로를 못 읽고 "홈"을 열었다. 그래서 <c>Arguments</c> 단일 문자열에
    /// 직접 인용해서 넘긴다.</item>
    /// <item>explorer.exe 는 <b>성공해도 ExitCode 1</b> 을 준다. 종료 코드로 실패를 판정하면 안 된다.</item>
    /// </list>
    /// </remarks>
    public string? RevealInExplorer(string path, bool isDirectory)
    {
        try
        {
            var arguments = isDirectory ? Quote(path) : $"/select,{Quote(path)}";
            using var process = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = arguments,
                UseShellExecute = false,
            });

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>ShellExecuteEx</c> 의 <c>properties</c> 동사를 쓴다. <b><c>Process.Start</c> 로는 안 된다</b> —
    /// <c>UseShellExecute</c> 경로는 <c>Verb</c> 를 넘길 수는 있어도
    /// <c>SEE_MASK_INVOKEIDLIST</c> 를 못 준다. 그 플래그가 없으면 폴더처럼 등록된 동사가 없는
    /// 대상에서 「이 파일에 대한 연결 프로그램이 없다」로 튕긴다.
    /// <para>
    /// P/Invoke 작성 방식은 <see cref="RecycleBin"/> 의 것을 그대로 따른다 — 구조체 안에 문자열이
    /// 섞여 있어 소스 생성 P/Invoke(<c>LibraryImport</c>)가 마샬링하지 못한다.
    /// </para>
    /// </remarks>
    public string? ShowProperties(string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "properties",
            lpFile = path,
            nShow = SwShow,
        };

        try
        {
            if (ShellExecuteExW(ref info))
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        // 실패는 마지막 Win32 오류로 말한다 — 셸이 이유를 그쪽에만 남긴다.
        return new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    private static string Quote(string path) => $"\"{path}\"";

    /// <summary>동사 목록을 ID 리스트에서 뽑게 한다 — <c>properties</c> 를 쓰려면 필수다.</summary>
    private const uint SeeMaskInvokeIdList = 0x0000000C;

    private const int SwShow = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;

        /// <summary><c>hIcon</c>/<c>hMonitor</c> 공용체 자리.</summary>
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo lpExecInfo);
}
