namespace FolderDeck.Core.Enumeration;

public enum FolderAccessFailureKind
{

    NotFound,

    AccessDenied,

    Unavailable,

    IoError,
}

public sealed record FolderAccessFailure(
    FolderAccessFailureKind Kind,
    string Path,
    string Message,
    Exception? Exception = null)
{
    public override string ToString() => $"[{Kind}] {Path}: {Message}";
}

public sealed class FolderListing
{
    private static readonly IReadOnlyList<FolderItem> Empty = [];

    private FolderListing(string path, IReadOnlyList<FolderItem> items, FolderAccessFailure? failure)
    {
        Path = path;
        Items = items;
        Failure = failure;
    }

    public string Path { get; }

    public IReadOnlyList<FolderItem> Items { get; }

    public FolderAccessFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static FolderListing Ok(string path, IReadOnlyList<FolderItem> items) => new(path, items, null);

    public static FolderListing Fail(FolderAccessFailure failure) => new(failure.Path, Empty, failure);
}
