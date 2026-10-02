using System.Runtime.InteropServices;
using System.Text;

namespace FolderDeck.App.Interop;















internal sealed class ShellContextMenu : IDisposable
{

    internal const uint IdCmdFirst = 0x1000;

    private const uint IdCmdLast = 0x7FFF;

    private readonly List<IntPtr> _absolutePidls = [];
    private IShellFolder? _parent;
    private IContextMenu? _menu;
    private IContextMenu2? _menu2;
    private IContextMenu3? _menu3;
    private IntPtr _menuUnknown = IntPtr.Zero;





    public static ShellContextMenu Acquire(IReadOnlyList<string> paths, IntPtr ownerHwnd)
    {
        var result = new ShellContextMenu();

        try
        {
            foreach (var p in paths)
            {
                int hr = ShellNative.SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _);
                if (hr != 0)
                {
                    throw new COMException($"SHParseDisplayName 실패: {p}", hr);
                }

                result._absolutePidls.Add(pidl);
            }


            var iid = ShellNative.IID_IShellFolder;
            int bindHr = ShellNative.SHBindToParent(
                result._absolutePidls[0], ref iid, out var parent, out var firstChild);
            if (bindHr != 0)
            {
                throw new COMException("SHBindToParent 실패", bindHr);
            }

            result._parent = parent;

            var children = new IntPtr[result._absolutePidls.Count];
            children[0] = firstChild;
            for (int i = 1; i < result._absolutePidls.Count; i++)
            {
                var iid2 = ShellNative.IID_IShellFolder;
                int hr2 = ShellNative.SHBindToParent(
                    result._absolutePidls[i], ref iid2, out _, out var child);
                if (hr2 != 0)
                {
                    throw new COMException("SHBindToParent(다중) 실패", hr2);
                }

                children[i] = child;
            }

            var menuIid = ShellNative.IID_IContextMenu;
            IntPtr ppv;
            int uiHr;


            using (FpuGuard.Enter("GetUIObjectOf"))
            {
                uiHr = parent.GetUIObjectOf(
                    ownerHwnd, (uint)children.Length, children, ref menuIid, IntPtr.Zero, out ppv);
            }

            if (uiHr != 0 || ppv == IntPtr.Zero)
            {
                throw new COMException("GetUIObjectOf(IID_IContextMenu) 실패", uiHr);
            }

            result._menuUnknown = ppv;
            result._menu = (IContextMenu)Marshal.GetObjectForIUnknown(ppv);
            result._menu2 = result._menu as IContextMenu2;
            result._menu3 = result._menu as IContextMenu3;

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }





    public IntPtr BuildMenu(IntPtr hMenu, uint firstIndex)
    {
        int hr;


        using (FpuGuard.Enter("QueryContextMenu"))
        {
            hr = _menu!.QueryContextMenu(hMenu, firstIndex, IdCmdFirst, IdCmdLast, ShellNative.CMF_NORMAL);
        }

        if (hr < 0)
        {
            throw new COMException("QueryContextMenu 실패", hr);
        }

        return hMenu;
    }


    public bool HandleMenuMessage(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;

        if (msg is not (ShellNative.WM_INITMENUPOPUP or ShellNative.WM_DRAWITEM
            or ShellNative.WM_MEASUREITEM or ShellNative.WM_MENUCHAR))
        {
            return false;
        }


        using var fpu = FpuGuard.Enter("HandleMenuMsg");

        if (_menu3 is not null)
        {
            return _menu3.HandleMenuMsg2(msg, wParam, lParam, out result) == 0;
        }

        if (_menu2 is not null)
        {
            return _menu2.HandleMenuMsg(msg, wParam, lParam) == 0;
        }

        return false;
    }





    public string? GetVerb(uint menuId)
    {
        if (menuId < IdCmdFirst)
        {
            return null;
        }

        var buffer = new byte[520];
        int hr;

        using (FpuGuard.Enter("GetCommandString"))
        {
            hr = _menu!.GetCommandString(
                (UIntPtr)(menuId - IdCmdFirst), ShellNative.GCS_VERBW, IntPtr.Zero, buffer, 260);
        }

        if (hr != 0)
        {
            return null;
        }

        var s = Encoding.Unicode.GetString(buffer);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }


    public int Invoke(uint menuId, IntPtr hwnd)
    {
        uint offset = menuId - IdCmdFirst;

        var info = new ShellNative.CmInvokeCommandInfoEx
        {
            cbSize = (uint)Marshal.SizeOf<ShellNative.CmInvokeCommandInfoEx>(),
            fMask = ShellNative.CMIC_MASK_UNICODE,
            hwnd = hwnd,


            lpVerb = (IntPtr)offset,
            lpVerbW = (IntPtr)offset,
            nShow = 1,
        };


        using var fpu = FpuGuard.Enter("InvokeCommand");

        return _menu!.InvokeCommand(ref info);
    }

















    public void Dispose()
    {
        _menu3 = null;
        _menu2 = null;
        _menu = null;

        if (_menuUnknown != IntPtr.Zero)
        {
            using (FpuGuard.Enter("Release(확장)"))
            {
                Marshal.Release(_menuUnknown);
            }

            _menuUnknown = IntPtr.Zero;
        }
        _parent = null;

        foreach (var pidl in _absolutePidls)
        {
            ShellNative.ILFree(pidl);
        }

        _absolutePidls.Clear();
    }
}
