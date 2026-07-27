using FolderDeck.Core.Models;

namespace FolderDeck.Core.Enumeration;

/// <summary>
/// 폴더 내용을 실시간으로 읽는다 (§2.2 — 파일 목록은 저장하지 않는다).
/// </summary>
/// <remarks>
/// 접근 실패는 예외가 아니라 <see cref="FolderListing.Failure"/> 로 돌아온다.
/// 유일하게 던지는 예외는 취소에 대한 <see cref="OperationCanceledException"/> 이다 —
/// 폴더를 빠르게 갈아탈 때 이전 열거를 버리기 위한 것이라 호출부가 무시하면 된다.
/// </remarks>
public interface IFolderEnumerator
{
    /// <summary>
    /// <paramref name="path"/> 의 파일과 하위 폴더를 읽어 정렬해 돌려준다(한 단계만, 재귀 아님).
    /// </summary>
    Task<FolderListing> ListAsync(
        string path,
        SortBy sortBy = SortBy.Name,
        bool sortDesc = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 내용을 읽지 않고 접근 가능한지만 값싸게 확인한다.
    /// 왼쪽 폴더 리스트가 열거 전에 "접근 불가"를 표시하는 데 쓴다.
    /// 접근 가능하면 null.
    /// </summary>
    Task<FolderAccessFailure?> ProbeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="root"/> 아래를 재귀로 훑어 이름에 <paramref name="query"/> 가 든 항목을
    /// <b>찾는 대로</b> 흘려보낸다(대소문자 무시 부분일치). 내용 검색·와일드카드는 아니다.
    /// </summary>
    /// <remarks>
    /// 발견 순서대로 나온다(정렬하지 않는다) — 다 끝날 때까지 기다리지 않고 점진적으로
    /// 보여주기 위한 것이다. 접근 못 하는 하위 폴더는 건너뛰고 계속하며
    /// <paramref name="stats"/> 에 센다. 취소는 걷는 도중에 즉시 듣는다.
    /// </remarks>
    IAsyncEnumerable<FolderSearchHit> SearchAsync(
        string root,
        string query,
        FolderSearchStats stats,
        CancellationToken cancellationToken = default);
}
