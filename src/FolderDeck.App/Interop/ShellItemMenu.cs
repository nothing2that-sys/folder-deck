using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace FolderDeck.App.Interop;

/// <summary>
/// 우클릭 한 판의 결과 — 앱 항목이 눌렸으면 그것, 셸 항목이었으면 그 실행 HRESULT.
/// </summary>
/// <remarks>둘 다 비어 있으면(<c>None</c> · <c>0</c>) 아무것도 안 골랐거나 잘 끝난 것이다.</remarks>
internal readonly record struct ShellMenuOutcome(ShellMenuAppCommand App, int ShellHr);

/// <summary>
/// 항목 우클릭 한 판 — <b>셸 메뉴를 그대로 띄우고 고른 것을 실행한다</b> (§4.6 · D26).
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
    /// <b>임시 계측이다(E-4).</b> <c>TrackPopupMenuEx</c> 의 중첩 메시지 루프가 도는 동안
    /// 1 이상이다 — 그 사이에 무엇이 큐에서 실행됐는지 가리려고 둔다.
    /// </summary>
    internal static int MenuDepth;

    /// <summary><b>임시 계측이다(E-4).</b> 메뉴가 마지막으로 닫힌 시각(UTC). 안 띄웠으면 null.</summary>
    internal static DateTime? LastMenuClosedUtc;

    /// <summary>
    /// 메뉴를 띄운다. 고른 것이 셸 항목이면 <b>여기서 실행하고</b>, 앱 항목이면 무엇이
    /// 눌렸는지만 돌려준다 — 앱 커맨드는 뷰모델 것이라 뷰가 부른다.
    /// </summary>
    /// <param name="source">메뉴의 owner 이자 메시지 훅이 붙을 창.</param>
    /// <param name="paths">대상 경로들. 다중 선택은 그대로 넘긴다.</param>
    /// <param name="isDirectory">우클릭한 항목이 폴더인가 — 폴더 전용 앱 항목 둘의 조건이다.</param>
    /// <param name="screenX">화면 좌표 x.</param>
    /// <param name="screenY">화면 좌표 y.</param>
    /// <remarks>
    /// 예외를 삼키지 않는다 — 부르는 쪽이 잡아 상태 바에 올린다(§5 · 팝업이 아니다).
    /// </remarks>
    public static ShellMenuOutcome Show(
        HwndSource source, IReadOnlyList<string> paths, bool isDirectory, int screenX, int screenY)
    {
        // ── E-3 임시 계측. 경로 문자열은 찍지 않는다 — 개인 경로가 로그에 남는다.
        ShellMenuProbe.Log(
            "Show",
            $"진입 paths={paths.Count} isDirectory={isDirectory} hwnd=0x{source.Handle.ToInt64():X}");

        using var scm = ShellContextMenu.Acquire(paths, source.Handle);

        var hMenu = ShellNative.CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            throw new COMException("CreatePopupMenu 실패", Marshal.GetLastPInvokeError());
        }

        // ── E-3 임시 계측. finally 에서 반환값을 찍으려고 둔다 — null 이면 예외로 빠졌다는 뜻이다.
        ShellMenuOutcome? outcome = null;

        try
        {
            // 앱 항목이 먼저다 — 셸은 그 뒤부터 채운다. 구분선 하나로 갈라 둔다.
            foreach (var item in ShellMenuPolicy.AppItems(isDirectory))
            {
                ShellNative.AppendMenuW(hMenu, ShellNative.MF_STRING, (UIntPtr)(uint)item.Command, item.Text);
            }

            ShellNative.AppendMenuW(hMenu, ShellNative.MF_SEPARATOR, UIntPtr.Zero, null);

            scm.BuildMenu(hMenu, (uint)ShellNative.GetMenuItemCount(hMenu));
            RemoveShellVerbs(scm, hMenu);

            uint chosen = Track(source, scm, hMenu, screenX, screenY);

            // 앱/셸 분기는 ShellMenuPolicy.ToAppCommand 하나뿐이다.
            var app = ShellMenuPolicy.ToAppCommand(chosen);

            // ── E-3 임시 계측. 앱이냐 셸이냐가 여기서 갈린다.
            ShellMenuProbe.Log("Show", $"chosen=0x{chosen:X} ToAppCommand={app}");

            if (app != ShellMenuAppCommand.None)
            {
                outcome = new ShellMenuOutcome(app, 0);
                return outcome.Value;
            }

            if (chosen == 0)
            {
                outcome = new ShellMenuOutcome(ShellMenuAppCommand.None, 0);
                return outcome.Value;
            }

            // ── E-3 임시 계측. 셸 항목을 고른 경우만 지나는 자리다.
            ShellMenuProbe.Log("Show", $"Invoke 직전 menuId=0x{chosen:X}");
            int shellHr = scm.Invoke(chosen, source.Handle);
            ShellMenuProbe.Log("Show", $"Invoke 직후 hr=0x{shellHr:X8}");

            outcome = new ShellMenuOutcome(ShellMenuAppCommand.None, shellHr);
            return outcome.Value;
        }
        finally
        {
            ShellNative.DestroyMenu(hMenu);

            // ── E-3 임시 계측. DestroyMenu 직후다.
            ShellMenuProbe.Log(
                "Show",
                outcome is { } done
                    ? $"정상 반환 App={done.App} ShellHr=0x{done.ShellHr:X8}"
                    : "예외로 빠져나감");
        }
    }

    /// <summary>
    /// 셸이 넣은 <c>delete</c>·<c>cut</c> 항목을 지운다 (D27 · D28).
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
                // ── E-3 임시 계측. 삼킨 것만 찍는다 — false 까지 찍으면 메뉴가 떠 있는 동안
                // 메시지가 쏟아져 로그가 못 쓰게 된다.
                ShellMenuProbe.Log(
                    "Hook",
                    $"msg=0x{msg:X4} wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X} " +
                    $"result=0x{result.ToInt64():X}");

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
            int lastError;

            // ── E-4 임시 계측. 중첩 메시지 루프가 도는 구간을 표시한다. finally 라 예외로도 샌다.
            Interlocked.Increment(ref MenuDepth);
            try
            {
                chosen = ShellNative.TrackPopupMenuEx(
                    hMenu,
                    ShellNative.TPM_RETURNCMD | ShellNative.TPM_LEFTALIGN | ShellNative.TPM_RIGHTBUTTON,
                    x,
                    y,
                    source.Handle,
                    IntPtr.Zero);

                // ── E-3 임시 계측. 바로 다음 줄에서 읽는다 — 사이에 P/Invoke 하나라도 끼면 값이 덮인다.
                lastError = Marshal.GetLastPInvokeError();
            }
            finally
            {
                Interlocked.Decrement(ref MenuDepth);
                LastMenuClosedUtc = DateTime.UtcNow;
            }

            ShellMenuProbe.Log(
                "Track",
                $"chosen={chosen} (0x{chosen:X}) lastError={lastError} " +
                $"hMenu=0x{hMenu.ToInt64():X} hwnd=0x{source.Handle.ToInt64():X}");

            return chosen;
        }
        finally
        {
            source.RemoveHook(Hook);
        }
    }
}
