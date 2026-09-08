using System.Runtime.InteropServices;

namespace FolderDeck.App.Interop;

// ─────────────────────────────────────────────────────────────────────────────
// 셸 IPreviewHandler 계열 interop 의 바닥(Phase 17 · D103). _spike_preview_D85\Interop\
// PreviewHandlerInterop.cs 에서 옮겼다(D85 스파이크가 실제로 돌려 본 선언). IShellItem
// 선언과 ShellInterop 클래스는 옮기지 않는다 — ShellItemNative.cs 에 이미 있다(D97 5-1
// 결정 5). GUID 출처: shobjidl_core.h(Windows SDK).
//
// COM 인터페이스는 vtable 순서가 곧 계약이다. IPreviewHandler 의 SetFocus·QueryFocus·
// TranslateAccelerator 는 이 회차가 부르지 않지만 선언에서 빼면 안 된다 — vtable 은 순서가
// 전부라 하나라도 빠지면 그 뒤 메서드가 전부 엉뚱한 함수를 부르게 된다.
// ─────────────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[ComImport]
[Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, ref RECT rect);
    void SetRect(ref RECT rect);
    void DoPreview();
    void Unload();
    void SetFocus();
    void QueryFocus(out IntPtr phwnd);
    [PreserveSig]
    int TranslateAccelerator(ref MSG pmsg);
}

// IInitializeWithFile — {b7d14566-0509-4cce-a71f-0a554233bd9b}
[ComImport]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
}

// IInitializeWithItem — {b824b49d-22ac-4161-ac8a-9916e8fa3f7f}
[ComImport]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithItem
{
    void Initialize(IShellItem psi, uint grfMode);
}
