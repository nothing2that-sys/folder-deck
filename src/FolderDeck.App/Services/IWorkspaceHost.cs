namespace FolderDeck.App.Services;

/// <summary>
/// 워크스페이스를 <b>이 프로세스에서</b> 연다 (§3.1 · Phase 6). 선택 창이 그대로 워크스페이스
/// 창이 되므로 새 프로세스를 띄우지 않는다 — 그쪽은 <see cref="ISelfLauncher"/> 다.
/// 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface IWorkspaceHost
{
    /// <summary>그 워크스페이스를 연다. 이미 열려 있으면 아무 일도 하지 않는다 (§6.2).</summary>
    WorkspaceOpenResult Open(Guid workspaceId);
}

/// <summary>여는 시도가 갈라지는 세 갈래 (§6.2).</summary>
/// <remarks>
/// "열었다"와 "아무 일도 안 했다"를 <b>갈라야</b> 부르는 쪽이 배지를 옳게 고친다 —
/// 문자열 하나로는 둘이 같은 값이 된다.
/// </remarks>
public enum WorkspaceOpenOutcome
{
    /// <summary>창을 띄웠다. 이 프로세스가 그 워크스페이스를 든다.</summary>
    Opened,

    /// <summary>이미 열려 있다. 아무 일도 하지 않았다 (§6.2).</summary>
    AlreadyOpen,

    /// <summary>열지 못했다. 이유는 <see cref="WorkspaceOpenResult.Message"/> 에 있다.</summary>
    Failed,
}

/// <summary>여는 시도의 결과.</summary>
/// <param name="Outcome">어디로 갈라졌는가.</param>
/// <param name="Message">
/// <see cref="WorkspaceOpenOutcome.Failed"/> 일 때의 이유. 그 밖에는 null.
/// </param>
public readonly record struct WorkspaceOpenResult(WorkspaceOpenOutcome Outcome, string? Message)
{
    public static WorkspaceOpenResult Opened() => new(WorkspaceOpenOutcome.Opened, null);

    public static WorkspaceOpenResult AlreadyOpen() => new(WorkspaceOpenOutcome.AlreadyOpen, null);

    public static WorkspaceOpenResult Failed(string message) =>
        new(WorkspaceOpenOutcome.Failed, message);
}
