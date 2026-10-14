using System.Collections.Generic;

namespace FolderDeck.App.Services;

/// <summary>
/// 명령줄 인자 (§3.1 · Phase 4 Stage 1). 프로세스 하나가 워크스페이스 하나를 들기 때문에
/// 런처가 자기 자신을 다시 실행할 때 무엇을 열지 이 인자로 넘긴다.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>(없음)</term><description>
/// <c>skipLauncher</c> 를 따른다 — 꺼져 있으면 런처, 켜져 있으면 마지막 워크스페이스로 직행.
/// </description></item>
/// <item><term><c>--workspace {guid}</c></term><description>런처 없이 그 워크스페이스로.</description></item>
/// <item><term><c>--launcher</c></term><description>
/// <c>skipLauncher</c> 를 <b>무시하고</b> 런처를 띄운다. 자동 열기가 켜져 있으면 인자 없는 실행이
/// 워크스페이스로 가버려 다른 워크스페이스를 열 자리가 없어지므로 이 인자가 필요하다.
/// </description></item>
/// <item><term><c>--scratch</c></term><description>
/// 1회용 창(2026-10-14 S4) — 저장되지 않는 탐색기 창을 띄운다. 여러 개를 동시에 띄울 수 있다.
/// <c>--workspace</c> 와 같이 주면 오류다(둘 중 무엇을 열지 정할 수 없다).
/// </description></item>
/// <item><term><c>--agent</c></term><description>
/// 상주 프로그램(2026-10-14 2차) — 트레이에 남아 1회용 창 단축키를 받는다. 창을 띄우지 않는다.
/// </description></item>
/// <item><term><c>--agent-exit</c></term><description>
/// 떠 있는 상주에게 끝내라고 알리고, 끝날 때까지 잠깐 기다린 뒤 끝난다(설치·제거가 부른다).
/// </description></item>
/// </list>
/// <para>
/// <c>--agent</c>·<c>--agent-exit</c> 는 <b>홀로</b> 써야 한다 — 다른 인자와 섞이면 오류를 내고 둘 다 버린다.
/// </para>
/// <para>
/// 잘못된 인자는 <see cref="Error"/> 로 올린다 — 조용히 무시하지 않는다(§5). 그때도 런처는
/// 뜬다: 인자를 못 읽었다고 아무것도 못 열게 하면 빠져나갈 길이 없다.
/// </para>
/// </remarks>
public sealed record CommandLineOptions
{
    public const string WorkspaceSwitch = "--workspace";
    public const string LauncherSwitch = "--launcher";

    public const string ScratchSwitch = "--scratch";

    public const string AgentSwitch = "--agent";

    public const string AgentExitSwitch = "--agent-exit";

    /// <summary>열라고 지정된 워크스페이스. 없으면 null.</summary>
    public Guid? WorkspaceId { get; private init; }

    /// <summary><c>skipLauncher</c> 를 무시하고 런처를 띄우라는 요청.</summary>
    public bool ForceLauncher { get; private init; }

    /// <summary>1회용 창을 띄우라는 요청 (S4).</summary>
    public bool Scratch { get; private init; }

    /// <summary>상주 프로그램으로 뜨라는 요청 (2026-10-14 2차).</summary>
    public bool Agent { get; private init; }

    /// <summary>떠 있는 상주를 끝내라는 요청 (2026-10-14 2차).</summary>
    public bool AgentExit { get; private init; }

    /// <summary>읽지 못한 인자에 대한 사용자에게 보일 말. 문제가 없으면 null.</summary>
    public string? Error { get; private init; }

    /// <summary>
    /// 인자를 읽는다. <c>--workspace</c> 와 <c>--launcher</c> 가 같이 오면 <b>워크스페이스가
    /// 이긴다</b> — <c>--launcher</c> 는 "자동 열기를 무시하라"는 약한 요청이고, 워크스페이스
    /// 지정은 무엇을 열지 못박는 강한 요청이다.
    /// </summary>
    public static CommandLineOptions Parse(IReadOnlyList<string>? args)
    {
        Guid? workspaceId = null;
        var forceLauncher = false;
        var scratch = false;
        var agent = false;
        var agentExit = false;
        string? error = null;

        for (var i = 0; i < (args?.Count ?? 0); i++)
        {
            var arg = args![i];

            if (Matches(arg, ScratchSwitch))
            {
                scratch = true;
                continue;
            }

            if (Matches(arg, AgentSwitch))
            {
                agent = true;
                continue;
            }

            if (Matches(arg, AgentExitSwitch))
            {
                agentExit = true;
                continue;
            }

            if (Matches(arg, LauncherSwitch))
            {
                forceLauncher = true;
                continue;
            }

            // --workspace {guid} 와 --workspace={guid} 를 둘 다 받는다.
            if (Matches(arg, WorkspaceSwitch))
            {
                if (i + 1 >= args.Count)
                {
                    error = Combine(error, $"{WorkspaceSwitch} 뒤에 작업 관리 id 가 없다.");
                    break;
                }

                error = Combine(error, ReadGuid(args[++i], ref workspaceId));
                continue;
            }

            if (arg.StartsWith(WorkspaceSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                error = Combine(error, ReadGuid(arg[(WorkspaceSwitch.Length + 1)..], ref workspaceId));
                continue;
            }

            error = Combine(error, $"알 수 없는 인자: {arg}");
        }

        if (scratch && workspaceId is not null)
        {
            error = Combine(error, $"{ScratchSwitch} 와 {WorkspaceSwitch} 는 같이 쓸 수 없다.");
            // 둘 다 버리고 선택 창으로 떨어진다 — 어느 쪽을 원했는지 모른다.
            workspaceId = null;
            scratch = false;
        }

        // 상주 둘은 홀로 써야 한다 — 다른 인자와 섞이면 무엇을 원했는지 모른다.
        if ((agent || agentExit) && (args!.Count != 1 || (agent && agentExit)))
        {
            error = Combine(error, $"{AgentSwitch} · {AgentExitSwitch} 는 다른 인자 없이 혼자 써야 한다.");
            agent = false;
            agentExit = false;
        }

        return new CommandLineOptions
        {
            Agent = agent,
            AgentExit = agentExit,
            WorkspaceId = workspaceId,
            ForceLauncher = forceLauncher,
            Scratch = scratch,
            Error = error,
        };
    }

    private static bool Matches(string arg, string name) =>
        string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

    private static string? ReadGuid(string text, ref Guid? target)
    {
        if (Guid.TryParse(text, out var parsed))
        {
            target = parsed;
            return null;
        }

        return $"작업 관리 id 로 읽을 수 없다: {text}";
    }

    private static string? Combine(string? existing, string? added) =>
        (existing, added) switch
        {
            (null, _) => added,
            (_, null) => existing,
            _ => $"{existing} {added}",
        };
}
