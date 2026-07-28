using FolderDeck.Core.Models;

namespace FolderDeck.Core.Operations;

// §4.7 · §10.1 — 대상함 / 드래그앤드롭 / 매크로 세 진입점이 공유하는 단일 실행 엔진의
// 계약. Phase 1 은 시그니처만 두고 구현은 Phase 3 이다. 대상함을 UI 에 박아 구현하지
// 않기 위해 자리를 먼저 잡아 둔다.

/// <summary>조작 대상 하나. 파일이든 폴더든(폴더면 재귀, §1.1).</summary>
public sealed record OperationItem(string Path, bool IsDirectory);

/// <summary>대상 집합 × 목적지 집합 × 동작 (§4.2).</summary>
public sealed record FileOperationRequest(
    FileOperationKind Op,
    IReadOnlyList<OperationItem> Sources,
    IReadOnlyList<string> Destinations,
    ConflictPolicy OnConflict);

/// <summary>목적지 하나에 대한 미리보기 내역.</summary>
public sealed record DestinationPreview(string Destination, int ItemCount, int OverwriteCount);

/// <summary>
/// 실행 전 미리보기 (§4.7) — "무엇 → 어디, N건, 덮어쓸 것 M건 + 목적지별 내역".
/// 접근 불가 목적지는 <see cref="SkippedDestinations"/> 로 빠진다(경로 자체는 유지).
/// </summary>
public sealed record FileOperationPreview(
    int TotalItemCount,
    int TotalOverwriteCount,
    IReadOnlyList<DestinationPreview> PerDestination,
    IReadOnlyList<string> SkippedDestinations);

/// <summary>항목 1건의 결과. 부분 실패가 기본이다 (§5).</summary>
public enum OperationItemStatus
{
    Succeeded,
    Skipped,
    Failed,
}

public sealed record OperationItemResult(
    string SourcePath,
    string Destination,
    OperationItemStatus Status,
    bool Overwritten,
    string? Reason);

/// <summary>결과 보고 = 성공 / 건너뜀 / 실패 + 이유 (§5).</summary>
public sealed record FileOperationReport(
    IReadOnlyList<OperationItemResult> Results,
    bool Canceled);

/// <summary>진행 표시용 (§1.1 — 폴더 재귀·볼륨 간 이동은 오래 걸린다).</summary>
/// <param name="CompletedItems">끝난 <b>단위</b> 수. 단위 = 대상 하나 × 목적지 하나.</param>
/// <param name="TotalItems">전체 단위 수. 시작할 때 정해진다.</param>
/// <param name="CurrentPath">지금 손대고 있는 실제 경로(폴더 안이면 그 안의 파일).</param>
/// <param name="CompletedEntries">
/// 폴더 재귀까지 세어 실제로 처리한 파일 수. 큰 폴더 하나가 단위 하나일 때
/// (<c>0/1</c> 에서 멈춰 보이는 것) 진행이 살아 있음을 보이려고 Phase 3 에서 추가했다.
/// </param>
public sealed record FileOperationProgress(
    int CompletedItems,
    int TotalItems,
    string? CurrentPath,
    int CompletedEntries = 0);

/// <summary>단일 실행 엔진 (§4.7). 구현은 Phase 3.</summary>
public interface IFileOperationEngine
{
    Task<FileOperationPreview> PreviewAsync(
        FileOperationRequest request,
        CancellationToken cancellationToken = default);

    Task<FileOperationReport> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
