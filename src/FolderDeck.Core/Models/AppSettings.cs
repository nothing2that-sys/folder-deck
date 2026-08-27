namespace FolderDeck.Core.Models;

/// <summary>전역 설정 (§7.2). <c>%APPDATA%\FolderDeck\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// 1 → 2: <c>tileGap</c> 이 늘었다. <b>워크스페이스 스키마는 이것과 무관하다</b> —
    /// 간격은 창마다가 아니라 앱 전체의 값이다.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>저장소 소유. 저장 시 찍힌다 (<see cref="Workspace.SchemaVersion"/> 과 같은 규약).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>"마지막 작업을 자동으로 열기" — 런처 건너뛰기 (§3.1).</summary>
    public bool SkipLauncher { get; set; }

    public Guid? LastWorkspaceId { get; set; }

    /// <summary>런처 표시 순서(옵션). 여기 없는 워크스페이스는 뒤에 붙인다.</summary>
    public List<Guid>? WorkspaceOrder { get; set; }

    // ── 타일 사이 여백 (§3.2 · D56) ──────────────────────────────────────────

    /// <summary>
    /// 고를 수 있는 <b>다섯</b> — 가족룩 §5 램프다. <b>임의의 숫자는 받지 않는다.</b>
    /// </summary>
    public static readonly IReadOnlyList<int> TileGapChoices = [4, 6, 8, 12, 16];

    /// <summary>
    /// 슬롯이 없는 옛 파일과 램프 밖 값이 읽히는 값 — <b>지금 화면 그대로다.</b>
    /// </summary>
    /// <remarks>
    /// 기본값을 다르게 두면 <b>옛 설정의 화면이 조용히 바뀐다</b> — v6 <c>foldersFirst</c> 가
    /// 밟은 자리다. 그래서 이 값은 8 이고, 8 은 이 회차 전의 간격(칸 여백 4 + 4)과 같다.
    /// </remarks>
    public const int DefaultTileGap = 8;

    private int _tileGap = DefaultTileGap;

    /// <summary>
    /// <b>눈에 보이는 타일 사이 간격</b>(칸 여백이 아니다). 램프 밖 값은
    /// <see cref="DefaultTileGap"/> 으로 떨어진다.
    /// </summary>
    /// <remarks>
    /// <b>램프를 지키는 유일한 자리다.</b> 슬롯이 없는 파일은 필드 초기값이 그대로 남고
    /// (<c>System.Text.Json</c> 은 없는 속성의 setter 를 부르지 않는다), 슬롯이 있으면 이
    /// setter 를 지나므로 <b>두 경우가 모두 여기서 닫힌다</b> — 직렬화기의 행동에만 기대지
    /// 않는다는 뜻이다.
    /// </remarks>
    public int TileGap
    {
        get => _tileGap;
        set => _tileGap = TileGapChoices.Contains(value) ? value : DefaultTileGap;
    }
}
