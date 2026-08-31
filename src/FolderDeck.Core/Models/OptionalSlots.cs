namespace FolderDeck.Core.Models;

// 옵셔널 슬롯들 (§7.1 주석: "null 이면 직렬화 생략"). grid/tiles 는 Phase 2 에서 실제로
// 쓰기 시작했고, window/copyTray/macros 는 아직 손실 없는 round-trip 을 위해 모델링만 해 둔다.

/// <summary>그리드 크기 (§7.1 <c>grid</c>). Phase 2 부터 실제로 쓴다. §3.2 기준 4x4.</summary>
public sealed class GridSpec
{
    public int Cols { get; set; } = 4;
    public int Rows { get; set; } = 4;
}

/// <summary>타일 성격 (§2.3). Phase 2. <c>Search</c> 는 Phase 12(D69-c) — 전역 검색 타일.</summary>
public enum TileKind
{
    Pinned,
    Rotating,
    Search,
}

/// <summary>
/// 타일 배치 (§7.1 <c>tiles[]</c>). Phase 2.
/// <c>currentPath</c>는 런타임 전용이라 여기에 없다(§4.6).
/// </summary>
public sealed class TileSpec
{
    /// <summary>
    /// 상시 칸이 못박은 폴더. <b>순환 칸은 null</b> — 어떤 폴더가 뜰지는 왼쪽 리스트 클릭이 정한다.
    /// <b>검색 칸도 null</b>(D69-c) — <see cref="FolderDeck.Core.Storage.WorkspaceStore.LoadWorkspaceFile"/>
    /// 이 값이 있어도 지운다.
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
/// 마지막 창 위치 (§7.1 <c>window</c>). Phase 4 Stage 2.
/// </summary>
/// <remarks>
/// 단위는 <b>WPF DIP</b> — <c>Window.Left/Top/Width/Height</c> 를 그대로 담는다.
/// 물리 픽셀로 오는 모니터 작업영역은 비교 시점에 한 번만 DIP 로 바꾼다(변환 지점은 한 곳).
/// <para>
/// <see cref="MonitorId"/> 는 <b>쓰지 않는다</b>(Phase 4 Stage 0 §4). 이 PC 의 모니터
/// <c>DeviceString</c> 이 세 대 모두 <c>"Generic PnP Monitor"</c> 라 식별에 못 쓰고,
/// <c>\\.\DISPLAYn</c> 의 n 은 그 모니터의 것이 아니라 이번 구성에서 배정된 슬롯 번호다.
/// 복원 판정은 "저장된 좌표가 현재 어느 작업영역에 드는가" 하나로만 한다 —
/// 안정성을 확인하지 못한 식별자에 동작을 걸지 않기 위해서다. <c>null</c> 이면 직렬화 생략.
/// </para>
/// </remarks>
public sealed class WindowSpec
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// 마지막이 최대화 상태였는가 (schemaVersion 5). 이때도 <see cref="X"/>~<see cref="Height"/>
    /// 에는 <b>복원 크기</b>가 들어간다 — 최대화 상태의 좌표를 넣으면 최대화를 풀었을 때
    /// 돌아갈 자리가 없어진다.
    /// </summary>
    /// <remarks>
    /// <c>bool?</c> 이 아니라 <c>bool</c> 인 것은 <c>askOnConflict</c> 와 사정이 달라서다 —
    /// 슬롯이 없는 구파일이 <c>false</c> 로 읽히는 것이 곧 "최대화 아님"이고, 그게 v4 까지의
    /// 실제 동작이다. 기본값이 위험한 쪽으로 기우는 필드가 아니다.
    /// </remarks>
    public bool Maximized { get; set; }

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
