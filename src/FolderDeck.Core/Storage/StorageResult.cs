namespace FolderDeck.Core.Storage;

public enum StorageFailureKind
{

    NotFound,

    CorruptData,

    InvalidContent,

    UnsupportedSchemaVersion,

    AccessDenied,

    IoError,
}

public sealed record StorageFailure(
    StorageFailureKind Kind,
    string Path,
    string Message,
    Exception? Exception = null)
{
    public override string ToString() => $"[{Kind}] {Path}: {Message}";
}

public sealed class StorageResult<T>
    where T : class
{
    private StorageResult(T? value, StorageFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public StorageFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static StorageResult<T> Ok(T value) => new(value, null);

    public static StorageResult<T> Fail(StorageFailure failure) => new(null, failure);

    public T ValueOrThrow() =>
        Value ?? throw new FolderDeckStorageException(Failure!);
}

public sealed class FolderDeckStorageException(StorageFailure failure)
    : Exception(failure.ToString(), failure.Exception)
{
    public StorageFailure Failure { get; } = failure;
}
