using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>전역 단축키 하나를 등록·해제하는 통로 (2026-10-14 2차 A3). 시험에서 갈아끼우려고 인터페이스다.</summary>
public interface IHotkeyRegistrar
{
    /// <summary>등록한다. 실패하면 이유(예: 다른 프로그램이 이미 씀).</summary>
    string? Register(uint modifiers, uint virtualKey);

    /// <summary>등록돼 있으면 푼다.</summary>
    void Unregister();
}

/// <summary>
/// 설정의 단축키 표기를 등록 상태로 맞춘다 (2026-10-14 2차 A3). 바뀌었을 때만 <b>해제 → 등록</b> 한다.
/// </summary>
public sealed class AgentHotkeyBinder(IHotkeyRegistrar registrar)
{
    /// <summary>지금 등록돼 있는 표기. 등록이 없거나 실패했으면 null.</summary>
    public string? Registered { get; private set; }

    /// <summary>
    /// <paramref name="hotkey"/>(정규형, 빈 문자열 = 없음)로 맞춘다. 같은 값으로 이미 등록돼 있으면 아무것도
    /// 안 한다. 실패하면 그 이유를 돌려주고 등록 없음 상태가 된다.
    /// </summary>
    public string? Update(string hotkey)
    {
        if (Registered == hotkey)
        {
            return null;
        }

        registrar.Unregister();
        Registered = null;

        if (ScratchHotkey.ToRegistration(hotkey) is not { } registration)
        {
            return null;
        }

        var failure = registrar.Register(registration.Modifiers, registration.VirtualKey);
        if (failure is null)
        {
            Registered = hotkey;
        }

        return failure;
    }

    public void Clear()
    {
        registrar.Unregister();
        Registered = null;
    }
}

/// <summary>
/// 실물 <c>RegisterHotKey</c> — 메시지 전용 숨은 창(<c>HWND_MESSAGE</c>)을 하나 만들어 <c>WM_HOTKEY</c> 를 받는다.
/// UI 스레드에서 만들고 써야 한다(등록한 스레드의 메시지 루프로 알림이 온다).
/// </summary>
public sealed class GlobalHotkey : IHotkeyRegistrar, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4644; // 'FD'
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _registered;

    public GlobalHotkey(Action onPressed)
    {
        _onPressed = onPressed ?? throw new ArgumentNullException(nameof(onPressed));
        _source = new HwndSource(new HwndSourceParameters("FolderDeck.AgentHotkey")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _source.AddHook(Hook);
    }

    public string? Register(uint modifiers, uint virtualKey)
    {
        Unregister();
        if (RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey))
        {
            _registered = true;
            return null;
        }

        var error = Marshal.GetLastPInvokeError();
        return error == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
            ? "다른 프로그램이 이미 그 단축키를 쓰고 있다."
            : $"단축키를 등록하지 못했다(오류 {error}).";
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    /// <summary>시험·측정용 — 실제 키 입력 없이 <c>WM_HOTKEY</c> 를 이 창에 보낸다(키보드 경로와는 다르다).</summary>
    internal void SimulatePress() => SendMessage(_source.Handle, WmHotkey, new IntPtr(HotkeyId), IntPtr.Zero);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
