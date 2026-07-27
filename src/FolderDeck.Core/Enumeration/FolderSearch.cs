namespace FolderDeck.Core.Enumeration;

/// <summary>
/// 재귀 검색 결과 1건.
/// </summary>
/// <param name="Item">찾은 파일 또는 폴더.</param>
/// <param name="RelativeFolder">
/// 검색 기준 폴더를 뺀 상대 폴더 경로. 기준 바로 밑 항목은 빈 문자열이다.
/// 탐색기의 "위치" 열과 같은 값.
/// </param>
public sealed record FolderSearchHit(FolderItem Item, string RelativeFolder);

/// <summary>
/// 진행 중에 갱신되는 검색 통계. 걷는 쪽이 쓰고 UI 가 읽는다 —
/// 결과가 하나도 안 나오는 동안에도 진행 표시를 할 수 있어야 하므로 스트림과 별도로 둔다.
/// </summary>
public sealed class FolderSearchStats
{
    private int _foldersScanned;
    private int _foldersSkipped;

    /// <summary>지금까지 들여다본 폴더 수.</summary>
    public int FoldersScanned => Volatile.Read(ref _foldersScanned);

    /// <summary>권한 없음·끊긴 네트워크 등으로 건너뛴 폴더 수 (§5 — 멈추지 않고 계속한다).</summary>
    public int FoldersSkipped => Volatile.Read(ref _foldersSkipped);

    internal void CountScanned() => Interlocked.Increment(ref _foldersScanned);

    internal void CountSkipped() => Interlocked.Increment(ref _foldersSkipped);
}
