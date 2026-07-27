namespace FolderDeck.Core.Enumeration;

/// <summary>폴더를 읽지 못한 이유. UI 문구를 원인별로 갈라 쓰기 위한 것.</summary>
public enum FolderAccessFailureKind
{
    /// <summary>경로가 없다.</summary>
    NotFound,

    /// <summary>권한이 없다.</summary>
    AccessDenied,

    /// <summary>네트워크 경로에 닿지 못한다 — 복구되면 다시 조회된다(§5).</summary>
    Unavailable,

    /// <summary>그 외 I/O 오류.</summary>
    IoError,
}

/// <summary>
/// 폴더 접근 실패 1건. Stage 1 의 <c>StorageFailure</c> 와 같은 규약 —
/// 예외로 터뜨리지 않고 결과에 담아 올려서, 호출부가 "접근 불가"로 렌더하고
/// <b>폴더를 목록에서 지우지 않게</b> 한다(§5).
/// </summary>
public sealed record FolderAccessFailure(
    FolderAccessFailureKind Kind,
    string Path,
    string Message,
    Exception? Exception = null)
{
    public override string ToString() => $"[{Kind}] {Path}: {Message}";
}

/// <summary>
/// 폴더 열거 결과. 성공하면 <see cref="Items"/>, 실패하면 <see cref="Failure"/>.
/// </summary>
public sealed class FolderListing
{
    private static readonly IReadOnlyList<FolderItem> Empty = [];

    private FolderListing(string path, IReadOnlyList<FolderItem> items, FolderAccessFailure? failure)
    {
        Path = path;
        Items = items;
        Failure = failure;
    }

    /// <summary>읽으려 한 경로.</summary>
    public string Path { get; }

    /// <summary>정렬까지 끝난 항목. 실패 시 빈 목록.</summary>
    public IReadOnlyList<FolderItem> Items { get; }

    public FolderAccessFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static FolderListing Ok(string path, IReadOnlyList<FolderItem> items) => new(path, items, null);

    public static FolderListing Fail(FolderAccessFailure failure) => new(failure.Path, Empty, failure);
}
