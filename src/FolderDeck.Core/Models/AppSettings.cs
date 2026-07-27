namespace FolderDeck.Core.Models;

/// <summary>전역 설정 (§7.2). <c>%APPDATA%\FolderDeck\settings.json</c>.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>저장소 소유. 저장 시 찍힌다 (<see cref="Workspace.SchemaVersion"/> 과 같은 규약).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>"마지막 작업을 자동으로 열기" — 런처 건너뛰기 (§3.1).</summary>
    public bool SkipLauncher { get; set; }

    public Guid? LastWorkspaceId { get; set; }

    /// <summary>런처 표시 순서(옵션). 여기 없는 워크스페이스는 뒤에 붙인다.</summary>
    public List<Guid>? WorkspaceOrder { get; set; }
}
