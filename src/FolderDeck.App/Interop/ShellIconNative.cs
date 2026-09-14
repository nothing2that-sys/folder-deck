using System.Runtime.InteropServices;

namespace FolderDeck.App.Interop;

// 손으로 쓴 [DllImport] 다 — Interop/ 의 관례를 그대로 따랐다(ShellNative.cs). SHFILEINFO
// 에 고정 길이 문자열이 박혀 있어 소스 생성 마샬링(LibraryImport)이 안 된다
// (Services/RecycleBin.cs 의 SHFileOperationW 와 같은 이유). 선언만 담는다 — 로직은 없다.
internal static class ShellIconNative
{
    internal const uint SHGFI_ICON = 0x00000100;
    internal const uint SHGFI_SMALLICON = 0x00000001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x00000010;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}
