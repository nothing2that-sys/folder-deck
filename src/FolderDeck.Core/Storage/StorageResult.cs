namespace FolderDeck.Core.Storage;

/// <summary>실패 원인 분류. UI 문구를 원인별로 갈라 쓰기 위한 것.</summary>
public enum StorageFailureKind
{
    /// <summary>파일이 없다.</summary>
    NotFound,

    /// <summary>JSON 파싱 실패 — 손상된 파일.</summary>
    CorruptData,

    /// <summary>파싱은 됐지만 필수 값이 비었거나 말이 안 된다.</summary>
    InvalidContent,

    /// <summary>이 빌드가 모르는(더 높은) schemaVersion.</summary>
    UnsupportedSchemaVersion,

    /// <summary>권한 문제.</summary>
    AccessDenied,

    /// <summary>그 외 I/O 오류.</summary>
    IoError,
}

/// <summary>
/// 로드/저장 실패 1건. <b>조용히 초기화하지 않는다</b>(§5)는 규칙을 지키려면
/// 실패가 이 객체로 상위까지 올라와야 한다.
/// </summary>
public sealed record StorageFailure(
    StorageFailureKind Kind,
    string Path,
    string Message,
    Exception? Exception = null)
{
    public override string ToString() => $"[{Kind}] {Path}: {Message}";
}

/// <summary>성공하면 <see cref="Value"/>, 실패하면 <see cref="Failure"/>.</summary>
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

    /// <summary>성공이면 값을, 실패면 던진다. 실패를 다룰 생각이 없는 호출부(테스트 등)용.</summary>
    public T ValueOrThrow() =>
        Value ?? throw new FolderDeckStorageException(Failure!);
}

/// <summary>결과 객체를 받고 싶지 않은 호출부를 위한 예외 형태.</summary>
public sealed class FolderDeckStorageException(StorageFailure failure)
    : Exception(failure.ToString(), failure.Exception)
{
    public StorageFailure Failure { get; } = failure;
}
