namespace FolderDeck.Core.Models;

// 옵셔널 슬롯들 (§7.1 주석: "null 이면 직렬화 생략"). grid/tiles 는 Phase 2 에서 실제로
// 쓰기 시작했고, window/copyTray/macros 는 아직 손실 없는 round-trip 을 위해 모델링만 해 둔다.

/// <summary>그리드 크기 (§7.1 <c>grid</c>). Phase 2 부터 실제로 쓴다. §3.2 기준 4x4.</summary>
public sealed class GridSpec
{
    public int Cols { get; set; } = 4;
    public int Rows { get; set; } = 4;
}

/// <summary>타일 성격 (§2.3). Phase 2.</summary>
public enum TileKind
{
    Pinned,
    Rotating,
}

/// <summary>
/// 타일 배치 (§7.1 <c>tiles[]</c>). Phase 2.
/// <c>currentPath</c>는 런타임 전용이라 여기에 없다(§4.6).
/// </summary>
public sealed class TileSpec
{
    /// <summary>
    /// 상시 칸이 못박은 폴더. <b>순환 칸은 null</b> — 어떤 폴더가 뜰지는 왼쪽 리스트 클릭이 정한다.
    /// </summary>
    public Guid? FolderId { get; set; }

    public int CellX { get; set; }

    public int CellY { get; set; }

    public int SpanX { get; set; } = 1;

    public int SpanY { get; set; } = 1;

    public TileKind Kind { get; set; }

    /// <summary>
    /// 순환 번호 (1..N). <see cref="TileKind.Rotating"/> 일 때만 값이 있고 상시 칸은 null.
    /// 폴더의 <see cref="FolderEntry.RotationSlot"/> 이 이 번호를 가리킨다.
    /// </summary>
    public int? RotationIndex { get; set; }

    public TileSpec Clone() => new()
    {
        FolderId = FolderId,
        CellX = CellX,
        CellY = CellY,
        SpanX = SpanX,
        SpanY = SpanY,
        Kind = Kind,
        RotationIndex = RotationIndex,
    };
}

/// <summary>
/// 마지막 창 위치 (§7.1 <c>window</c>). Phase 4.
/// <c>MonitorId</c> 표현 방식은 모니터 식별 API 확인 후 정한다(§11) — 그때까지 문자열 자리만.
/// </summary>
public sealed class WindowSpec
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? MonitorId { get; set; }
}

/// <summary>매크로 소스 종류 (§4.7). <c>pattern</c>은 v1 제외.</summary>
public enum MacroSourceKind
{
    FixedPath,
    Selection,
}

/// <summary>매크로 목적지 종류 (§4.7).</summary>
public enum MacroDestKind
{
    AllVisible,
    Tray,
    FolderIds,
}

/// <summary>매크로 소스 (§4.7). Phase 3.</summary>
public sealed class MacroSource
{
    public MacroSourceKind Kind { get; set; }
    public string? Path { get; set; }
}

/// <summary>매크로 목적지 (§4.7). Phase 3.</summary>
public sealed class MacroDest
{
    public MacroDestKind Kind { get; set; }
    public List<Guid>? FolderIds { get; set; }
}

/// <summary>매크로 규칙 (§4.7 / §7.1 <c>macros[]</c>). Phase 3.</summary>
public sealed class MacroDefinition
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public FileOperationKind Op { get; set; }
    public MacroSource? Source { get; set; }
    public MacroDest? Dest { get; set; }
    public ConflictPolicy OnConflict { get; set; }

    /// <summary>실행 전 미리보기·확인. 기본 켬 (§4.7).</summary>
    public bool Confirm { get; set; } = true;
}
