using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FolderDeck.App.Interop;

/// <summary>
/// 우클릭 한 판의 결과 — 앱 항목이 눌렸으면 그것, 셸 항목이었으면 그 실행 HRESULT.
/// </summary>
/// <remarks>둘 다 비어 있으면(<c>None</c> · <c>0</c>) 아무것도 안 골랐거나 잘 끝난 것이다.</remarks>
internal readonly record struct ShellMenuOutcome(ShellMenuAppCommand App, int ShellHr);

/// <summary>
/// 항목 우클릭 한 판 — <b>셸 메뉴를 그대로 띄우고 고른 것을 실행한다</b> (§4.6 · D32).
/// </summary>
/// <remarks>
/// 탐색기 동등성이 목표다. 앱이 자기 <c>ContextMenu</c> 를 그리는 대신 셸이 준 HMENU 를
/// 그대로 <c>TrackPopupMenuEx</c> 로 띄운다.
/// <para>
/// <b>UI 스레드에서만 부른다</b> — COM STA 다(<see cref="ShellContextMenu"/> 주석).
/// </para>
/// </remarks>
internal static class ShellItemMenu
{
    /// <summary>
    /// 메뉴를 띄운다. 고른 것이 셸 항목이면 <b>여기서 실행하고</b>, 앱 항목이면 무엇이
    /// 눌렸는지만 돌려준다 — 앱 커맨드는 뷰모델 것이라 뷰가 부른다.
    /// </summary>
    /// <param name="source">메뉴의 owner 이자 메시지 훅이 붙을 창.</param>
    /// <param name="paths">대상 경로들. 다중 선택은 그대로 넘긴다.</param>
    /// <param name="isDirectory">우클릭한 항목이 폴더인가 — 폴더 전용 앱 항목 둘의 조건이다.</param>
    /// <param name="canCompareContent">「내용 비교」를 끼울지 (기능 후보 ①의 연장) — 자세한
    /// 조건은 <see cref="ShellMenuPolicy.AppItems"/> 참고.</param>
    /// <param name="screenX">화면 좌표 x.</param>
    /// <param name="screenY">화면 좌표 y.</param>
    /// <remarks>
    /// 예외를 삼키지 않는다 — 부르는 쪽이 잡아 상태 바에 올린다(§5 · 팝업이 아니다).
    /// </remarks>
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
            // 앱 항목이 먼저다 — 셸은 그 뒤부터 채운다. 구분선 하나로 갈라 둔다.
            foreach (var item in ShellMenuPolicy.AppItems(isDirectory, canCompareContent))
            {
                ShellNative.AppendMenuW(hMenu, ShellNative.MF_STRING, (UIntPtr)(uint)item.Command, item.Text);
            }

            ShellNative.AppendMenuW(hMenu, ShellNative.MF_SEPARATOR, UIntPtr.Zero, null);

            scm.BuildMenu(hMenu, (uint)ShellNative.GetMenuItemCount(hMenu));
            RemoveShellVerbs(scm, hMenu);

            uint chosen = Track(source, scm, hMenu, screenX, screenY);

            // 앱/셸 분기는 ShellMenuPolicy.ToAppCommand 하나뿐이다.
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

    /// <summary>
    /// 셸이 넣은 <c>delete</c>·<c>cut</c> 항목을 지운다 (D33).
    /// </summary>
    /// <remarks>
    /// <b>최상위만 본다.</b> 그 둘은 최상위에 오고(조사 실측), 서브메뉴까지 파고들면
    /// 「보내기」 같은 남의 묶음 안을 건드리게 된다.
    /// <para>
    /// 지울 id 를 <b>먼저 모으고 나중에 지운다</b> — 순회하면서 지우면 뒤 항목의 위치가 밀린다.
    /// 지우는 것은 <c>MF_BYCOMMAND</c> 라 위치가 아니라 id 로 찾는다.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 메뉴를 띄우고 고른 id 를 받는다. <b>떠 있는 동안만</b> 메시지 훅을 건다.
    /// </summary>
    /// <remarks>
    /// 훅이 없으면 확장 서브메뉴가 <b>빈 항목으로 남는다</b>(조사 2·3·7회차 재현).
    /// 아이콘 때문이 아니다 — 아이콘은 훅 없이도 나온다.
    /// <para>
    /// <c>TPM_RETURNCMD</c> 라 <c>TrackPopupMenuEx</c> 는 <b>고른 id 만</b> 돌려주고 아무것도
    /// 실행하지 않는다 — 실행은 부르는 쪽이 정한다.
    /// </para>
    /// </remarks>
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

        // owner 가 포그라운드라야 메뉴가 뜬다(조사 3회차). 우클릭 시점엔 대개 이미 그렇다.
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
