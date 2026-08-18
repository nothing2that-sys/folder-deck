using System.Runtime.InteropServices;

namespace FolderDeck.App.Interop;

// ─────────────────────────────────────────────────────────────────────────────
// 셸 컨텍스트 메뉴 interop 의 바닥. 손으로 쓴 [DllImport] + [ComImport] 만 쓴다 —
// Services/RecycleBin.cs · WindowActivation.cs 가 이미 그 방식이고 여기도 그 관례를 따랐다.
// NuGet 패키지를 하나도 더하지 않는다.
//
// COM 인터페이스는 **vtable 순서가 곧 계약**이라 안 쓰는 메서드도 순서대로 다 적어야 한다.
// IContextMenu2/3 는 C# 에서 인터페이스 상속이 vtable 상속이 되지 않으므로 부모 메서드를
// 다시 적었다. 하나라도 빠지면 엉뚱한 함수가 불린다.
//
// 이 파일의 모양은 spike/ShellMenuProbe/Interop/ShellNative.cs 에서 왔다 —
// 조사(B-0)가 실제로 돌려 본 선언만 옮기고, 조사 전용(클립보드 열거·메뉴 자동 조작)은 뺐다.
// ─────────────────────────────────────────────────────────────────────────────

[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

    [PreserveSig]
    int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

    [PreserveSig]
    int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    [PreserveSig]
    int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);

    [PreserveSig]
    int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
        ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

    [PreserveSig]
    int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

    [PreserveSig]
    int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        uint uFlags, out IntPtr ppidlOut);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    // 성공 시 HRESULT 하위 16비트에 "추가한 항목 수"가 실린다 — PreserveSig 필수.
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref ShellNative.CmInvokeCommandInfoEx pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
        [MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);
}

[ComImport]
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref ShellNative.CmInvokeCommandInfoEx pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
        [MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);

    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
}

[ComImport]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref ShellNative.CmInvokeCommandInfoEx pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
        [MarshalAs(UnmanagedType.LPArray)] byte[] pszName, uint cchMax);

    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

    [PreserveSig]
    int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
}

internal static class ShellNative
{
    internal static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    internal static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // ── QueryContextMenu uFlags ──
    // CMF_EXTENDEDVERBS 는 쓰지 않는다 — 조사는 CMF_NORMAL 로만 돌았다(B-0 보고문 §8-9).
    internal const uint CMF_NORMAL = 0x00000000;

    // ── InvokeCommand fMask ──
    internal const uint CMIC_MASK_UNICODE = 0x00004000;

    // ── TrackPopupMenuEx ──
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;

    // ── 메뉴 메시지. 이 넷을 셸로 넘기지 않으면 확장 서브메뉴가 빈 항목으로 남는다
    //    (조사 2·3·7회차 재현). 아이콘 문제가 아니다 — 아이콘은 훅 없이도 나온다.
    internal const uint WM_INITMENUPOPUP = 0x0117;
    internal const uint WM_DRAWITEM = 0x002B;
    internal const uint WM_MEASUREITEM = 0x002C;
    internal const uint WM_MENUCHAR = 0x0120;

    // ── GetCommandString uType ──
    internal const uint GCS_VERBW = 0x00000004;

    // ── AppendMenu / DeleteMenu uFlags ──
    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint MF_BYCOMMAND = 0x00000000;

    // ── MENUITEMINFO ──
    internal const uint MIIM_ID = 0x00000002;
    internal const uint MIIM_SUBMENU = 0x00000004;
    internal const uint MIIM_FTYPE = 0x00000100;

    internal const uint MFT_SEPARATOR = 0x00000800;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MenuItemInfoW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CmInvokeCommandInfoEx
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public Point ptInvoke;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl,
        uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IShellFolder ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    internal static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    internal static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuItemInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMenuItemInfoW(IntPtr hMenu, uint item,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MenuItemInfoW lpmii);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    // TrackPopupMenuEx 는 owner 창이 포그라운드라야 제대로 돈다 — 조사 3회차에서 이걸 안 걸었더니
    // 메뉴가 아예 안 떴다. 우클릭 시점엔 이미 포그라운드라 대개 무동작이다.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
