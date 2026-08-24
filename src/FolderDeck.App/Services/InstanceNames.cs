using System.Diagnostics;

namespace FolderDeck.App.Services;

/// <summary>
/// 인스턴스 단일화에 쓰는 OS 자원 이름 (§6.2). 이름 규칙은 Phase 4 Stage 0 실측을 따른다.
/// </summary>
/// <remarks>
/// <para>
/// <b>뮤텍스는 <c>Local\</c> — 같은 사용자 세션 안에서만 유일하다.</b> §6.2 가 막으려는 사고는
/// 같은 창 두 개가 종료 시 창 위치를 서로 덮어쓰는 것이고 그건 한 세션 안의 문제다.
/// <c>Global\</c> 로 막으면 다른 세션(원격 데스크톱·빠른 사용자 전환)의 인스턴스까지 걸리는데
/// 그쪽 창은 앞으로 가져올 수도 없어 "열리지도 않고 앞으로도 안 오는" 막다른 길이 된다.
/// </para>
/// <para>
/// <b>파이프 이름공간은 머신 전역이라 <c>Local\</c> 같은 접두사가 없다.</b> 그래서 세션 id 를
/// 이름에 직접 넣는다 — 안 넣으면 다른 세션이 같은 워크스페이스를 열 때 이름이 부딪혀,
/// 뮤텍스는 얻었는데 파이프 서버는 만들지 못하는 창(신호를 못 받는 창)이 된다
/// (같은 이름 두 번째 서버는 <c>IOException "모든 파이프 인스턴스가 사용 중"</c> — 실측).
/// 이렇게 두면 뮤텍스와 파이프의 유효 범위가 같아진다.
/// </para>
/// <para>
/// <b>대소문자를 임의로 바꾸지 말 것.</b> 커널 객체 이름은 대소문자를 구분한다(Stage 0 실측) —
/// 한쪽만 <c>ToUpper</c> 하면 탐지가 통째로 무력화된다. <see cref="Guid.ToString()"/> 가 항상
/// 소문자를 주므로 그것을 그대로 쓴다.
/// </para>
/// </remarks>
public static class InstanceNames
{
    /// <summary>이 프로세스가 속한 터미널 세션. 파이프 이름을 세션별로 갈라 놓는 데 쓴다.</summary>
    public static int SessionId { get; } = ReadSessionId();

    /// <summary>워크스페이스 하나를 나타내는 뮤텍스 이름.</summary>
    public static string WorkspaceMutex(Guid workspaceId) => $@"Local\FolderDeck.{workspaceId}";

    /// <summary>워크스페이스 창이 "앞으로" 신호를 받는 파이프 이름.</summary>
    public static string WorkspacePipe(Guid workspaceId) => Pipe(workspaceId.ToString());

    private static string Pipe(string key) => $"FolderDeck.s{SessionId}.{key}";

    private static int ReadSessionId()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
        catch (Exception)
        {
            // 세션 id 를 못 읽으면 이름을 가르는 것만 못 할 뿐, 한 세션 안에서는 그대로 동작한다.
            return 0;
        }
    }
}
