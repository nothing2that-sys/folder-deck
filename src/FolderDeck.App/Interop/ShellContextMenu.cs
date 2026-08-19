using System.Runtime.InteropServices;
using System.Text;

namespace FolderDeck.App.Interop;

/// <summary>
/// 경로 목록 하나에 대한 셸 컨텍스트 메뉴. 얻기 → HMENU 채우기 → 실행 까지가 한 살림이다.
/// </summary>
/// <remarks>
/// 경로: <c>SHParseDisplayName</c>(절대 PIDL) → <c>SHBindToParent</c>(부모 IShellFolder + 자식 PIDL)
/// → <c>IShellFolder.GetUIObjectOf(IID_IContextMenu)</c>. 다중 선택은 <b>부모가 같아야</b>
/// 한 번에 되므로 첫 항목의 부모를 잡고 나머지는 자식 PIDL 만 모은다.
/// <para>
/// <b>UI 스레드에서만 쓴다.</b> COM STA 라 <c>QueryContextMenu</c> 를 백그라운드로 뺄 수 없다 —
/// 얻기와 채우기가 그대로 UI 멈춤이고(조사 실측 70~90ms · 프로세스 첫 호출 532.7ms) 그것은
/// 감수한 대가다(D26). 확장 DLL 이 우리 주소 공간에 in-proc 로 로드되는 것도 마찬가지다.
/// </para>
/// <para>이 파일의 모양은 <c>spike/ShellMenuProbe/Interop/ShellContextMenu.cs</c> 에서 왔다.</para>
/// </remarks>
internal sealed class ShellContextMenu : IDisposable
{
    /// <summary>셸 항목이 쓸 id 대역의 시작. 앱 항목은 이 <b>아래</b>를 쓴다.</summary>
    internal const uint IdCmdFirst = 0x1000;

    private const uint IdCmdLast = 0x7FFF;

    private readonly List<IntPtr> _absolutePidls = [];
    private IShellFolder? _parent;
    private IContextMenu? _menu;
    private IContextMenu2? _menu2;
    private IContextMenu3? _menu3;
    private IntPtr _menuUnknown = IntPtr.Zero;

    /// <summary>
    /// 경로들의 셸 메뉴 객체를 얻는다. 실패는 <see cref="COMException"/> 으로 올린다 —
    /// <b>삼키지 않는다</b>(§5).
    /// </summary>
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

            // 부모는 첫 항목에서 잡는다. 자식 PIDL 은 절대 PIDL 안을 가리키므로 따로 해제하지 않는다.
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
                    result._absolutePidls[i], ref iid2, out var otherParent, out var child);
                if (hr2 != 0)
                {
                    throw new COMException("SHBindToParent(다중) 실패", hr2);
                }

                Marshal.ReleaseComObject(otherParent);
                children[i] = child;
            }

            var menuIid = ShellNative.IID_IContextMenu;
            int uiHr = parent.GetUIObjectOf(
                ownerHwnd, (uint)children.Length, children, ref menuIid, IntPtr.Zero, out var ppv);
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

    /// <summary>
    /// 빈 HMENU 를 만들고 셸 항목으로 채운다. <paramref name="firstIndex"/> 부터 넣으므로
    /// 앞자리는 앱 항목이 미리 차지할 수 있다.
    /// </summary>
    public IntPtr BuildMenu(IntPtr hMenu, uint firstIndex)
    {
        int hr = _menu!.QueryContextMenu(hMenu, firstIndex, IdCmdFirst, IdCmdLast, ShellNative.CMF_NORMAL);
        if (hr < 0)
        {
            throw new COMException("QueryContextMenu 실패", hr);
        }

        return hMenu;
    }

    /// <summary>메뉴 메시지를 셸로 넘긴다. <c>IContextMenu3</c> 가 있으면 그쪽이 우선이다.</summary>
    public bool HandleMenuMessage(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;

        if (msg is not (ShellNative.WM_INITMENUPOPUP or ShellNative.WM_DRAWITEM
            or ShellNative.WM_MEASUREITEM or ShellNative.WM_MENUCHAR))
        {
            return false;
        }

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

    /// <summary>
    /// 셸 항목의 verb 문자열. 못 얻으면 <c>null</c> — <b>이름으로 찾는 쪽으로 넘어가지 않는다</b>
    /// (지역화된 이름은 다른 언어에서 깨진다).
    /// </summary>
    public string? GetVerb(uint menuId)
    {
        if (menuId < IdCmdFirst)
        {
            return null;
        }

        var buffer = new byte[520];
        int hr = _menu!.GetCommandString(
            (UIntPtr)(menuId - IdCmdFirst), ShellNative.GCS_VERBW, IntPtr.Zero, buffer, 260);
        if (hr != 0)
        {
            return null;
        }

        var s = Encoding.Unicode.GetString(buffer);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    /// <summary>고른 셸 항목을 실행한다. 실패하면 HRESULT 를 그대로 돌려준다.</summary>
    public int Invoke(uint menuId, IntPtr hwnd)
    {
        uint offset = menuId - IdCmdFirst;

        var info = new ShellNative.CmInvokeCommandInfoEx
        {
            cbSize = (uint)Marshal.SizeOf<ShellNative.CmInvokeCommandInfoEx>(),
            fMask = ShellNative.CMIC_MASK_UNICODE,
            hwnd = hwnd,

            // MAKEINTRESOURCE — 언어 무관하게 id 로 부른다(verb 문자열 번역에 안 걸린다).
            lpVerb = (IntPtr)offset,
            lpVerbW = (IntPtr)offset,
            nShow = 1, // SW_SHOWNORMAL
        };

        return _menu!.InvokeCommand(ref info);
    }

    /// <summary>
    /// 놓는 것은 <b>우리가 실제로 얻은 참조 하나</b>뿐이다 — <c>GetUIObjectOf</c> 가 준
    /// <see cref="_menuUnknown"/>. 나머지는 <c>null</c> 로만 버리고 RCW 수명은 GC 에 맡긴다.
    /// </summary>
    /// <remarks>
    /// <b><c>Marshal.ReleaseComObject</c> 를 쓰지 않는다.</b> <see cref="_menu2"/>/<see cref="_menu3"/>
    /// 는 <c>as</c> 캐스트라 <see cref="_menu"/> 와 <b>같은 RCW</b> 다 — 새 객체가 아니다. 그런데
    /// <c>ReleaseComObject</c> 는 참조 수를 보지 않는 <b>강제 절단</b>이라, 셋을 각각 놓으면 한 객체를
    /// 여러 번 끊는 셈이 된다. 셸 확장은 <c>InvokeCommand</c> 가 돌아온 뒤에도 자기 일을 계속하는 것이
    /// 흔한데, 그 사이 객체가 끊기면 확장이 <b>죽은 것을 만진다</b>.
    /// <para>
    /// 같은 이유로 <see cref="_parent"/> 도 놓지 않는다. <c>FinalReleaseComObject</c> 는 더 세게
    /// 끊는 것이라 정반대다.
    /// </para>
    /// <para>두 번 불러도 안전하다 — <see cref="_menuUnknown"/> 은 놓은 뒤 <c>IntPtr.Zero</c> 로 비운다.</para>
    /// </remarks>
    public void Dispose()
    {
        _menu3 = null;
        _menu2 = null;
        _menu = null;
        if (_menuUnknown != IntPtr.Zero) { Marshal.Release(_menuUnknown); _menuUnknown = IntPtr.Zero; }
        _parent = null;

        foreach (var pidl in _absolutePidls)
        {
            ShellNative.ILFree(pidl);
        }

        _absolutePidls.Clear();
    }
}
