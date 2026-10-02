using FolderDeck.Core.Models;

namespace FolderDeck.Core.Operations;

public sealed record OperationItem(string Path, bool IsDirectory);

public sealed record FileOperationRequest(
    FileOperationKind Op,
    IReadOnlyList<OperationItem> Sources,
    IReadOnlyList<string> Destinations,
    ConflictPolicy OnConflict);

public sealed record DestinationPreview(string Destination, int ItemCount, int OverwriteCount);

public sealed record FileOperationPreview(
    int TotalItemCount,
    int TotalOverwriteCount,
    IReadOnlyList<DestinationPreview> PerDestination,
    IReadOnlyList<string> SkippedDestinations);

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

public sealed record FileOperationReport(
    IReadOnlyList<OperationItemResult> Results,
    bool Canceled);

public sealed record FileOperationProgress(
    int CompletedItems,
    int TotalItems,
    string? CurrentPath,
    int CompletedEntries = 0);

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
