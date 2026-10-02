using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FolderDeck.App.Interop;





internal readonly record struct ShellMenuOutcome(ShellMenuAppCommand App, int ShellHr);











internal static class ShellItemMenu
{














    public static ShellMenuOutcome Show(
        HwndSource source, IReadOnlyList<string> paths, bool isDirectory, bool canCompareContent,
        int screenX, int screenY)
    {
        using var scm = ShellContextMenu.Acquire(paths, source.Handle);

        var hMenu = ShellNative.CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            throw new COMException("CreatePopupMenu 실패", Marshal.GetLastPInvokeError());
        }

        try
        {

            foreach (var item in ShellMenuPolicy.AppItems(isDirectory, canCompareContent))
            {
                ShellNative.AppendMenuW(hMenu, ShellNative.MF_STRING, (UIntPtr)(uint)item.Command, item.Text);
            }

            ShellNative.AppendMenuW(hMenu, ShellNative.MF_SEPARATOR, UIntPtr.Zero, null);

            scm.BuildMenu(hMenu, (uint)ShellNative.GetMenuItemCount(hMenu));
            RemoveShellVerbs(scm, hMenu);

            uint chosen = Track(source, scm, hMenu, screenX, screenY);


            var app = ShellMenuPolicy.ToAppCommand(chosen);

            if (app != ShellMenuAppCommand.None)
            {
                return new ShellMenuOutcome(app, 0);
            }

            if (chosen == 0)
            {
                return new ShellMenuOutcome(ShellMenuAppCommand.None, 0);
            }

            int shellHr = scm.Invoke(chosen, source.Handle);

            return new ShellMenuOutcome(ShellMenuAppCommand.None, shellHr);
        }
        finally
        {
            ShellNative.DestroyMenu(hMenu);
        }
    }












    private static void RemoveShellVerbs(ShellContextMenu scm, IntPtr hMenu)
    {
        var doomed = new List<uint>(2);
        int count = ShellNative.GetMenuItemCount(hMenu);

        for (uint i = 0; i < count; i++)
        {
            var mii = new ShellNative.MenuItemInfoW
            {
                cbSize = (uint)Marshal.SizeOf<ShellNative.MenuItemInfoW>(),
                fMask = ShellNative.MIIM_ID | ShellNative.MIIM_FTYPE | ShellNative.MIIM_SUBMENU,
            };

            if (!ShellNative.GetMenuItemInfoW(hMenu, i, true, ref mii)
                || (mii.fType & ShellNative.MFT_SEPARATOR) != 0
                || mii.hSubMenu != IntPtr.Zero
                || mii.wID < ShellContextMenu.IdCmdFirst)
            {
                continue;
            }

            if (ShellMenuPolicy.IsRemovedVerb(scm.GetVerb(mii.wID)))
            {
                doomed.Add(mii.wID);
            }
        }

        foreach (var id in doomed)
        {
            ShellNative.DeleteMenu(hMenu, id, ShellNative.MF_BYCOMMAND);
        }
    }












    private static uint Track(HwndSource source, ShellContextMenu scm, IntPtr hMenu, int x, int y)
    {
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (scm.HandleMenuMessage((uint)msg, wParam, lParam, out var result))
            {
                handled = true;
                return result;
            }

            return IntPtr.Zero;
        }


        ShellNative.SetForegroundWindow(source.Handle);

        source.AddHook(Hook);
        try
        {
            uint chosen;

            using (FpuGuard.Enter("TrackPopupMenuEx"))
            {
                chosen = ShellNative.TrackPopupMenuEx(
                    hMenu,
                    ShellNative.TPM_RETURNCMD | ShellNative.TPM_LEFTALIGN | ShellNative.TPM_RIGHTBUTTON,
                    x,
                    y,
                    source.Handle,
                    IntPtr.Zero);
            }

            return chosen;
        }
        finally
        {
            source.RemoveHook(Hook);
        }
    }
}
