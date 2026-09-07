using System.Runtime.InteropServices;

namespace FolderDeck.App.Interop;

// ─────────────────────────────────────────────────────────────────────────────
// 셸 IShellItem 계열 interop 의 바닥 — ShellNative.cs(셸 컨텍스트 메뉴)와 갈라 둔다(D95 §2
// 결정 3). 손으로 쓴 [DllImport] + [ComImport] 만 쓴다 — ShellNative.cs·ShellIconNative.cs 가
// 이미 그 방식이고 여기도 그 관례를 따랐다. NuGet 패키지를 하나도 더하지 않는다.
//
// COM 인터페이스는 **vtable 순서가 곧 계약**이라 안 쓰는 메서드도 순서대로 다 적어야 한다.
// 하나라도 빠지면 엉뚱한 함수가 불린다.
//
// IShellItem 의 GUID·메서드 순서는 the preview interop spike: 
// Interop\PreviewHandlerInterop.cs 를 그대로 본떴다(D85 스파이크가 실제로 돌려 본 선언).
// IShellItemImageFactory 의 GUID·SIIGBF 값은 저장소·스파이크 어디에도 실물이 없어 Windows SDK
// shobjidl_core.h(10.0.26100.0)와 대조했다 — 둘 다 일치(D95-보정1 실행 보고 참고).
// ─────────────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;
}

// IShellItem 최소 vtable — 이 회차가 부르는 것은 없다(SHCreateItemFromParsingName 이 만들어
// 준 것을 IShellItemImageFactory 로 QueryInterface 하는 통로로만 쓴다). 그래도 vtable 순서를
// 맞추기 위해 안 쓰는 메서드도 전부 적는다.
[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    // 예외가 아니라 HRESULT 로 실패를 받는다 — 실패를 ③ 단(글귀)으로 떨어뜨려야 하므로
    // [PreserveSig] 로 적었다(D95 §5 R2).
    [PreserveSig]
    int GetImage(SIZE size, int flags, out IntPtr phbm);
}

internal static class ShellItemNative
{
    /// <summary>이 회차가 쓰는 것은 둘뿐이다 — 나머지 SIIGBF 상수는 적지 않는다.</summary>
    internal const int SIIGBF_BIGGERSIZEOK = 0x01;

    internal const int SIIGBF_THUMBNAILONLY = 0x08;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);
}
