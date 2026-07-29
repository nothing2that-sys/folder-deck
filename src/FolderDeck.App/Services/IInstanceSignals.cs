using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderDeck.App.Services;

/// <summary>
/// 런처가 인스턴스 상태를 보고 신호를 보내는 창구 (§4.2). 테스트에서 갈아끼우기 위해
/// 인터페이스로 둔다.
/// </summary>
public interface IInstanceSignals
{
    /// <summary>
    /// 후보 중 <b>지금 신호를 받을 수 있는</b> 워크스페이스들. 배지의 뜻이 그것이다 (§4.2).
    /// </summary>
    IReadOnlyCollection<Guid> WhichAreOpen(IEnumerable<Guid> candidates);

    /// <summary>
    /// 그 워크스페이스 창을 앞으로 가져온다. 성공하면 true — 이게 "누를 때의 판정"이다.
    /// </summary>
    bool TrySendActivate(Guid workspaceId);
}

/// <inheritdoc/>
/// <remarks>
/// <para>
/// <b>배지는 파이프 존재로 판정한다 — 뮤텍스가 아니다.</b> 배지의 뜻이 "신호를 받을 수 있는가"
/// 이므로 파이프가 그 뜻과 정확히 같고, 뮤텍스는 잡혀 있는데 응답이 없는 좀비(§3.2-3)가
/// 배지에 저절로 "닫힘"으로 나타난다. 그 상태에서 누르면 §4.2 대로 새로 열리는 것이 맞다.
/// 런처가 뮤텍스를 <b>소유</b>하면 그 워크스페이스가 열린 것이 되어 버려 애초에 안 되고,
/// 소유하지 않고 여는 <c>Mutex.TryOpenExisting</c> 도 (실측: 0.8ms, 소유권 안 가져감) 이름을
/// 살려 두는 핸들이 되어 Stage 0 §5 가 지적한 함정을 런처가 만들게 된다.
/// </para>
/// <para>
/// <b>존재 확인은 이름공간 열거로 한다. <c>File.Exists(@"\\.\pipe\이름")</c> 를 쓰지 않는다.</b>
/// 실측: <c>File.Exists</c> 는 파이프를 <b>열어서</b> 확인하기 때문에 <b>대기 중인 연결을 하나
/// 잡아먹는다</b>(5번 부르면 서버에 연결이 5번 들어온다). 열거(<c>Directory.EnumerateFiles</c>)는
/// 디렉터리만 읽어 그런 부작용이 없고, 워크스페이스가 몇 개든 <b>한 번</b>이면 된다.
/// </para>
/// </remarks>
public sealed class InstanceSignals : IInstanceSignals
{
    private const string PipeRoot = @"\\.\pipe\";

    /// <inheritdoc/>
    public IReadOnlyCollection<Guid> WhichAreOpen(IEnumerable<Guid> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var ids = candidates.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var live = LivePipeNames();
        return live.Count == 0
            ? []
            : [.. ids.Where(id => live.Contains(InstanceNames.WorkspacePipe(id)))];
    }

    /// <inheritdoc/>
    public bool TrySendActivate(Guid workspaceId) =>
        ActivationSignal.TrySend(InstanceNames.WorkspacePipe(workspaceId));

    /// <summary>
    /// 그 이름의 파이프가 지금 있는가. 위와 같은 열거라 <b>부작용이 없다</b> —
    /// <c>File.Exists</c> 로 확인하면 대기 중인 연결을 잡아먹는다(실측).
    /// </summary>
    /// <remarks>
    /// 종료 중인 프로세스가 "런처가 떠 있나"를 볼 때 쓴다. 없으면 신호를 아예 보내지 않아
    /// 없는 상대를 500ms 기다리는 일을 만들지 않는다.
    /// </remarks>
    public static bool IsLive(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return LivePipeNames().Contains(pipeName);
    }

    /// <summary>파이프 이름공간 한 번 열거. 실패하면 빈 집합 — 배지가 안 켜질 뿐이다.</summary>
    /// <remarks>
    /// 파이프 이름은 (뮤텍스 이름과 달리) 대소문자를 구분하지 않으므로 비교도 그렇게 한다.
    /// 양쪽 다 <see cref="InstanceNames"/> 가 만들어 실제로 어긋날 일은 없지만, 열거가 돌려주는
    /// 표기에 판정을 걸지 않는 편이 안전하다.
    /// </remarks>
    private static HashSet<string> LivePipeNames()
    {
        try
        {
            return new HashSet<string>(
                Directory.EnumerateFiles(PipeRoot).Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
