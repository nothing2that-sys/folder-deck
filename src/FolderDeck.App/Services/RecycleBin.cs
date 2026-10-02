using System.Runtime.InteropServices;
using FolderDeck.App.Interop;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Services;

















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


    public string? Send(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);


        using var fpu = FpuGuard.Enter("SHFileOperationW");

        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,


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


    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct lpFileOp);
}
