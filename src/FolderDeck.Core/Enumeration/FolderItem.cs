namespace FolderDeck.Core.Enumeration;

/// <summary>
/// 폴더 안의 항목 하나. 파일이거나 하위 폴더다.
/// 저장하지 않는 런타임 값이다 — 폴더 내용은 열 때 실시간으로 읽는다(§2.2).
/// </summary>
/// <param name="Name">항목 이름(경로 없음).</param>
/// <param name="FullPath">전체 경로.</param>
/// <param name="IsDirectory">하위 폴더면 true.</param>
/// <param name="Size">파일 크기(바이트). 폴더는 null — 재귀 계산하지 않는다.</param>
/// <param name="ModifiedUtc">마지막 수정 시각(UTC). 표시할 때 지역 시각으로 바꾼다.</param>
public sealed record FolderItem(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTime ModifiedUtc);
