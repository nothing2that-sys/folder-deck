using System;
using System.Diagnostics;
using System.Threading;
using FolderDeck.Core.Models;
using Microsoft.Win32;

namespace FolderDeck.App.Services;

/// <summary>로그인 시 자동 시작(<c>HKCU\…\Run</c>) 한 줄 (2026-10-14 2차). 시험에서 갈아끼우려고 인터페이스다.</summary>
public interface IRunKey
{
    /// <summary>지금 값. 없으면 null.</summary>
    string? Read();

    /// <summary>값을 쓴다(덮어씀).</summary>
    void Write(string command);

    /// <summary>값을 지운다. 없으면 아무 일 없다.</summary>
    void Delete();
}

/// <summary>실물 Run 키. 사용자 단위라 권한이 필요 없다.</summary>
public sealed class RunKey : IRunKey
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>설치 프로그램의 <c>[Registry]</c> 가 제거 때 지우는 이름과 같아야 한다.</summary>
    public const string ValueName = "FolderDeck.Agent";

    public static RunKey Default { get; } = new();

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>상주 프로그램이 살아 있는지 보고, 띄우고, 신호를 보내는 통로 (2026-10-14 2차).</summary>
public interface IAgentControl
{
    bool IsRunning { get; }

    /// <summary>상주를 새 프로세스로 띄운다. 성공하면 null, 실패하면 이유.</summary>
    string? Start();

    /// <summary>상주에게 「1회용 창 열기」를 보낸다. 받았으면 true.</summary>
    bool TrySignalScratch();

    /// <summary>상주에게 「끝내기」를 보낸다. 받았으면 true.</summary>
    bool TrySignalExit();
}

public sealed class AgentControl : IAgentControl
{
    public static AgentControl Default { get; } = new();

    public bool IsRunning => InstanceSignals.IsLive(InstanceNames.AgentPipe);

    public string? Start()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null)
            {
                return "실행 파일 경로를 알 수 없다.";
            }

            using var process = Process.Start(new ProcessStartInfo(exe, CommandLineOptions.AgentSwitch)
            {
                UseShellExecute = false,
            });
            return process is null ? "상주 프로그램을 띄우지 못했다." : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    public bool TrySignalScratch() => ActivationSignal.TrySend(InstanceNames.AgentPipe, AgentPayloads.Scratch);

    public bool TrySignalExit() => ActivationSignal.TrySend(InstanceNames.AgentPipe, AgentPayloads.Exit);

    /// <summary>
    /// 상주의 뮤텍스가 사라질 때까지 기다린다(<c>--agent-exit</c> 가 부른다 — 설치 프로그램이 파일을 덮기 전에
    /// 상주가 정말 끝났는지 알아야 한다). 시간 안에 끝났으면 true.
    /// </summary>
    public static bool WaitForExit(TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (!Mutex.TryOpenExisting(InstanceNames.AgentMutex, out var existing))
            {
                return true;
            }

            existing.Dispose();
            Thread.Sleep(100);
        }

        return false;
    }
}

/// <summary>상주 파이프에 싣는 본문 둘.</summary>
public static class AgentPayloads
{
    public const string Scratch = "scratch";
    public const string Exit = "exit";
}

/// <summary>
/// 단축키를 <b>어느 쪽이 받을지</b> 맞춘다 (2026-10-14 2차 · JH 결정: 사용자가 고른다).
/// </summary>
/// <remarks>
/// <see cref="AppSettings.UseResidentAgent"/> 하나가 셋을 함께 정한다 — 둘 다 켜지면 같은 키에 창이 두 번 뜬다.
/// <list type="table">
/// <item><term>켬</term><description>바로가기(.lnk) 키 지움 · Run 키 씀 · 상주가 <c>RegisterHotKey</c> 로 받음.</description></item>
/// <item><term>끔</term><description>바로가기 키 = 설정값 · Run 키 지움 · 상주는 설정 감시로 이를 보고 스스로 끝남.</description></item>
/// </list>
/// 설정값이 원본이고 바로가기·Run 키는 그것을 따른다. 그래서 모든 시작 경로와 설정 저장 때 다시 맞춘다 —
/// 재설치가 바로가기를 새로 만들어도 다음 시작에 고친다.
/// </remarks>
public interface IHotkeyRouting
{
    IAgentControl Agent { get; }

    /// <summary>바로가기 설치 여부(끔 쪽 안내용).</summary>
    bool ShortcutInstalled { get; }

    /// <summary>바로가기 키 · Run 키를 설정에 맞춘다. 실패가 있으면 그 이유(여럿이면 이어 붙임).</summary>
    /// <param name="agentHoldsKey">
    /// 상주가 아직 단축키를 <c>RegisterHotKey</c> 로 쥐고 있는가. 참이면 「끔」이어도 바로가기에 키를 <b>아직 쓰지 않는다</b> —
    /// 탐색기는 바로가기 키가 바뀔 때 그 키를 등록하는데, 상주가 쥐고 있으면 실패하고 다시 시도하지 않는다(JH 손확인:
    /// 끄면 단축키가 아예 안 먹었다). 상주가 물러나며 키를 놓은 뒤 스스로 다시 부른다(<c>AgentRuntime.Retire</c>).
    /// </param>
    string? Apply(AppSettings settings, bool agentHoldsKey = false);

    /// <summary>
    /// 상주가 끝나기 직전(트레이 「상주 끝내기」 · <c>--agent-exit</c>)에 단축키를 바로가기에 <b>넘겨준다</b>(JH 결정) — 상주가
    /// 없는 동안에도 단축키가 (느리게라도) 1회용 창을 띄우게. 그렇게 뜬 프로세스가 상주를 다시 띄우고(<c>App.EnsureAgent</c>),
    /// 새 상주의 시작 경로가 <see cref="Apply"/> 로 바로가기 키를 다시 지운다. Run 키는 건드리지 않는다 — 설정은 여전히 「켬」이다.
    /// 부르는 쪽이 <b>단축키를 먼저 놓은 뒤</b> 불러야 한다(탐색기 등록이 상주에 막히지 않게).
    /// </summary>
    string? HandOverToShortcut(AppSettings settings);
}

/// <summary>순수 판정 — 설정에서 바로가기 키와 Run 키가 어떠해야 하는지.</summary>
public static class HotkeyRoutingRules
{
    public static (string ShortcutHotkey, bool RunAtLogin) Decide(AppSettings settings, bool agentHoldsKey = false) =>
        settings.UseResidentAgent
            ? (ScratchHotkey.None, true)
            : (agentHoldsKey ? ScratchHotkey.None : settings.ScratchHotkey, false);

    /// <summary>
    /// 상주를 써야 하는데 떠 있지 않으면 띄울지 — 모든 시작 경로가 묻는다(JH 손확인: 트레이 「상주 끝내기」 뒤 앱을 다시
    /// 실행해도 트레이에 안 들어갔다). 설정을 못 쓰는 실행에서는 띄우지 않는다 — 그 실행은 설정을 믿을 수 없다.
    /// </summary>
    public static bool ShouldStartAgent(bool useResidentAgent, bool settingsWritable, bool agentRunning) =>
        useResidentAgent && settingsWritable && !agentRunning;

    /// <summary>Run 키에 쓸 명령줄. 경로에 공백이 있어도 되게 따옴표로 감싼다.</summary>
    public static string AgentCommand(string exePath) => $"\"{exePath}\" {CommandLineOptions.AgentSwitch}";
}

public sealed class HotkeyRouting(IScratchShortcut shortcut, IRunKey runKey, IAgentControl agent, string? exePath)
    : IHotkeyRouting
{
    public static HotkeyRouting Default { get; } =
        new(ScratchShortcut.Default, RunKey.Default, AgentControl.Default, Environment.ProcessPath);

    public IAgentControl Agent { get; } = agent;

    public bool ShortcutInstalled => shortcut.IsInstalled;

    public string? Apply(AppSettings settings, bool agentHoldsKey = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var (shortcutHotkey, runAtLogin) = HotkeyRoutingRules.Decide(settings, agentHoldsKey);
        string? failure = null;

        var applied = shortcut.Apply(shortcutHotkey);
        if (applied.Outcome == ScratchShortcutOutcome.Failed)
        {
            failure = $"바로가기 단축키를 맞추지 못했다 — {applied.Error}";
        }

        try
        {
            if (runAtLogin && exePath is not null)
            {
                var command = HotkeyRoutingRules.AgentCommand(exePath);
                if (runKey.Read() != command)
                {
                    runKey.Write(command);
                }
            }
            else if (!runAtLogin && runKey.Read() is not null)
            {
                runKey.Delete();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            failure = failure is null
                ? $"로그인 시 자동 시작을 맞추지 못했다 — {ex.Message}"
                : $"{failure} · 로그인 시 자동 시작을 맞추지 못했다 — {ex.Message}";
        }

        return failure;
    }

    public string? HandOverToShortcut(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var applied = shortcut.Apply(settings.ScratchHotkey);
        return applied.Outcome == ScratchShortcutOutcome.Failed
            ? $"바로가기 단축키를 맞추지 못했다 — {applied.Error}"
            : null;
    }
}

/// <summary>배선을 모르는 기존 시험이 쓸 기본값 — 아무것도 건드리지 않는다(실제 Run 키·바로가기에 손대지 않게).</summary>
internal sealed class NullHotkeyRouting : IHotkeyRouting, IAgentControl
{
    public static readonly NullHotkeyRouting Instance = new();

    private NullHotkeyRouting()
    {
    }

    public IAgentControl Agent => this;

    public bool ShortcutInstalled => false;

    public bool IsRunning => false;

    public string? Apply(AppSettings settings, bool agentHoldsKey = false) => null;

    public string? HandOverToShortcut(AppSettings settings) => null;

    public string? Start() => null;

    public bool TrySignalScratch() => false;

    public bool TrySignalExit() => false;
}
