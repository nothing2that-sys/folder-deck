using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>바로가기 단축키를 맞춘 결과.</summary>
public enum ScratchShortcutOutcome
{
    /// <summary>이미 같은 값이었다 — 쓰지 않았다.</summary>
    Unchanged,

    /// <summary>바꿔 썼다.</summary>
    Written,

    /// <summary>설치본 바로가기가 없다(개발 빌드 · 사용자가 지움). 만들지 않는다.</summary>
    NotInstalled,

    /// <summary>읽거나 쓰다 실패했다 — <see cref="ScratchShortcutResult.Error"/>.</summary>
    Failed,
}

public sealed record ScratchShortcutResult(ScratchShortcutOutcome Outcome, string? Error = null);

/// <summary>
/// 1회용 창 바로가기(시작 메뉴 「Folder Deck 1회용」)의 바로 가기 키 (2026-10-14 S4). 시험에서 갈아끼우려고
/// 인터페이스로 둔다 — <see cref="ISettingsEditor"/> 와 같은 결이다.
/// </summary>
public interface IScratchShortcut
{
    /// <summary>설치본 바로가기가 있는가.</summary>
    bool IsInstalled { get; }

    /// <summary>바로가기의 단축키를 <paramref name="hotkey"/>(정규형, 빈 문자열 = 없음)로 맞춘다.</summary>
    ScratchShortcutResult Apply(string hotkey);
}

/// <summary>바로가기를 모르는 기존 시험이 안전하게 쓸 기본값 — 설치본이 없는 것처럼 답한다.</summary>
internal sealed class NullScratchShortcut : IScratchShortcut
{
    public static readonly NullScratchShortcut Instance = new();

    private NullScratchShortcut()
    {
    }

    public bool IsInstalled => false;

    public ScratchShortcutResult Apply(string hotkey) => new(ScratchShortcutOutcome.NotInstalled);
}

/// <summary>
/// 실물 .lnk 를 <c>IShellLinkW</c> + <c>IPersistFile</c> 로 읽고 쓴다.
/// </summary>
/// <remarks>
/// 설치가 사용자 단위(<c>PrivilegesRequired=lowest</c>)라 바로가기는
/// <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Folder Deck\</c> 아래에 있고 관리자 권한 없이
/// 고쳐 쓸 수 있다. <b>바로가기가 없으면 만들지 않는다</b> — 앱이 만든 .lnk 는 제거 프로그램이 모르는
/// 파일이 된다.
/// <para>
/// 탐색기가 바뀐 단축키를 로그아웃 없이 바로 읽는지는 자동 시험이 재지 못한다(손확인 몫).
/// </para>
/// </remarks>
public sealed class ScratchShortcut(string shortcutPath) : IScratchShortcut
{
    /// <summary>설치 프로그램이 만드는 자리(<c>installer/FolderDeck.iss</c> 의 <c>[Icons]</c>).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "Folder Deck",
        "Folder Deck 1회용.lnk");

    public static ScratchShortcut Default { get; } = new(DefaultPath);

    public string ShortcutPath { get; } = shortcutPath;

    public bool IsInstalled => File.Exists(ShortcutPath);

    public ScratchShortcutResult Apply(string hotkey)
    {
        if (!IsInstalled)
        {
            return new(ScratchShortcutOutcome.NotInstalled);
        }

        try
        {
            var wanted = ScratchHotkey.ToShortcutValue(hotkey);
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                var file = (IPersistFile)link;
                file.Load(ShortcutPath, 0x2 /* STGM_READWRITE */);
                link.GetHotkey(out var current);
                if (current == wanted)
                {
                    return new(ScratchShortcutOutcome.Unchanged);
                }

                link.SetHotkey(wanted);
                file.Save(ShortcutPath, fRemember: true);
                return new(ScratchShortcutOutcome.Written);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return new(ScratchShortcutOutcome.Failed, ex.Message);
        }
    }

    /// <summary>바로가기의 지금 단축키 값(시험·진단용). 없으면 null.</summary>
    public ushort? ReadHotkey()
    {
        if (!IsInstalled)
        {
            return null;
        }

        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(ShortcutPath, 0 /* STGM_READ */);
            link.GetHotkey(out var value);
            return value;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>시험이 실물 .lnk 를 만들 때 쓴다 — 제품 코드는 바로가기를 만들지 않는다.</summary>
    internal static void CreateForTest(string shortcutPath, string targetPath)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(targetPath);
            ((IPersistFile)link).Save(shortcutPath, fRemember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    /// <summary><c>IShellLinkW</c> — vtable 순서 그대로다(shobjidl_core.h). 쓰지 않는 것도 자리를 지킨다.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out ushort pwHotkey);

        void SetHotkey(ushort wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
