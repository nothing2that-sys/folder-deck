namespace FolderDeck.App.Services;

public interface IWorkspaceHost
{

    WorkspaceOpenResult Open(Guid workspaceId);
}

public enum WorkspaceOpenOutcome
{

    Opened,

    AlreadyOpen,

    Failed,
}

public readonly record struct WorkspaceOpenResult(WorkspaceOpenOutcome Outcome, string? Message)
{
    public static WorkspaceOpenResult Opened() => new(WorkspaceOpenOutcome.Opened, null);

    public static WorkspaceOpenResult AlreadyOpen() => new(WorkspaceOpenOutcome.AlreadyOpen, null);

    public static WorkspaceOpenResult Failed(string message) =>
        new(WorkspaceOpenOutcome.Failed, message);
}
