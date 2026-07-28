using System.Runtime.InteropServices;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Services;

/// <summary>
/// Windows 휴지통 (§4.4). 셸 호출이라 플랫폼 중립인 Core 가 아니라 여기 둔다.
/// </summary>
/// <remarks>
/// <c>SHFileOperation</c> 을 쓴다. <c>IFileOperation</c> COM 이 더 최신이지만 이 용도
/// (조용히 · 되돌릴 수 있게 · 오류를 코드로)에는 이쪽이 훨씬 짧고, 실환경에서 동작을 확인했다:
/// 반환 0 = 성공하고 파일이 휴지통에 들어간다(복구 가능), 2 = 경로 없음.
/// <para>
/// <b>플래그 조합이 이 클래스의 핵심이다.</b>
/// <c>ALLOWUNDO</c> 로 휴지통에 보내고, <c>NOCONFIRMATION · NOERRORUI · SILENT</c> 로
/// 대화상자를 전부 끈다(실패는 결과로 올려야 하므로 §5). 다만
/// <c>WANTNUKEWARNING</c> 을 같이 준다 — 휴지통에 못 넣는 경우(용량 초과·네트워크·이동식
/// 드라이브)에 <c>NOCONFIRMATION</c> 만 있으면 셸이 <b>말없이 영구 삭제</b>해 버린다.
/// 이 플래그가 그 경우에만 경고를 띄워 "영구 삭제 아님"(§4.4)을 지킨다.
/// </para>
/// </remarks>
public sealed class RecycleBin : IRecycleBin
{
    private const uint FoDelete = 0x0003;

    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    private const int ErrorFileNotFound = 2;
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;

    /// <inheritdoc/>
    public string? Send(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,

            // pFrom 은 이중 null 로 끝나는 목록이다. 하나씩 보내 실패를 항목별로 잡는다.
            pFrom = path + "\0\0",
            fFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofWantNukeWarning,
        };

        int code;
        try
        {
            code = SHFileOperationW(ref operation);
        }
        catch (Exception ex)
        {
            return $"휴지통 호출 실패: {ex.Message}";
        }

        if (operation.fAnyOperationsAborted)
        {
            return "취소됐다";
        }

        return code switch
        {
            0 => null,
            ErrorFileNotFound => "원본이 없다",
            ErrorAccessDenied => "권한이 없다",
            ErrorSharingViolation => "다른 프로그램이 쓰고 있다",

            // 셸 전용 코드는 문서화된 것만 골라 옮기고 나머지는 숫자를 그대로 보인다 —
            // 뜻을 지어내는 것보다 낫다.
            0x7C => "경로가 잘못됐다",
            0x78 => "사용자가 거부했다",
            0x10000 => "알 수 없는 오류",
            _ => $"휴지통으로 보내지 못했다 (코드 {code})",
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    // 구조체 안에 문자열이 섞여 있어 소스 생성 P/Invoke(LibraryImport)가 마샬링하지 못한다.
    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct lpFileOp);
}
