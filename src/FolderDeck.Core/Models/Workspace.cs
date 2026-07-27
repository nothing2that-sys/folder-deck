namespace FolderDeck.Core.Models;

/// <summary>
/// 작업 관리 (§2.1 / §7.1). <c>%APPDATA%\FolderDeck\workspaces\{id}.json</c> 하나에 대응.
/// <para>
/// Phase 1 이 실제로 읽고 쓰는 범위는 <see cref="SchemaVersion"/>, <see cref="Id"/>,
/// <see cref="Title"/>, <see cref="Description"/>, <see cref="LastUsed"/>,
/// <see cref="Folders"/> 다. 나머지는 옵셔널 슬롯이며 null 이면 직렬화에서 생략된다.
/// </para>
/// </summary>
public sealed class Workspace
{
    /// <summary>
    /// 현재 스키마 버전. 이보다 <b>큰</b> 파일은 로드 실패로 보고하고,
    /// <b>작은</b> 파일(구버전)은 없는 필드를 기본값으로 읽어 그대로 열고 저장 시 승격한다.
    /// <para>
    /// 2 — <see cref="LeftLayout"/> 추가(필드 추가만이라 v1 파일도 그대로 읽힌다).
    /// 3 — <see cref="Grid"/>·<see cref="Tiles"/> 를 실제로 쓰기 시작 + 타일에
    /// <c>rotationIndex</c>, 폴더에 <c>rotationSlot</c> 추가. 역시 필드 추가뿐이라
    /// v1/v2 파일을 거부하지 않는다 — <c>tiles</c> 가 비어 있으면 <c>pinned</c> 에서 유도한다.
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// 스키마 버전은 저장소 소유다. 저장할 때 <see cref="CurrentSchemaVersion"/> 이 찍히고,
    /// 로드할 때 값이 없으면(=0) 실패로 보고된다. 호출부가 채울 필요는 없다.
    /// </summary>
    public int SchemaVersion { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset LastUsed { get; set; }

    public List<FolderEntry> Folders { get; set; } = [];

    /// <summary>
    /// 왼쪽 컬럼 접힘 상태 (schemaVersion 2). null 이면 기본값(안 접힘)으로 취급한다 —
    /// v1 파일에는 이 슬롯이 없다.
    /// </summary>
    public LeftLayoutSpec? LeftLayout { get; set; }

    /// <summary>그리드 크기 (schemaVersion 3). null 이면 기본 4x4 로 채워진다.</summary>
    public GridSpec? Grid { get; set; }

    /// <summary>
    /// 타일 배치 (schemaVersion 3). null 이거나 비어 있으면 <c>folders[].pinned</c> 에서
    /// 한 번 유도해 채운다(v1/v2 마이그레이션) — <c>FolderDeck.Core.Layout.GridLayout</c>.
    /// </summary>
    public List<TileSpec>? Tiles { get; set; }

    // ── 아래는 Phase 3+ 슬롯. 지금은 자리만 두고 건드리지 않는다(§7.1). ──

    public WindowSpec? Window { get; set; }

    /// <summary>복사 대상함에 상주하는 폴더들 (§3.2). Phase 3.</summary>
    public List<Guid>? CopyTray { get; set; }

    public List<MacroDefinition>? Macros { get; set; }
}
