namespace FolderDeck.Core.Models;

/// <summary>
/// 왼쪽 컬럼의 접힘 상태 (§7.1 <c>leftLayout</c>, schemaVersion 2 에서 추가).
/// </summary>
/// <remarks>
/// <c>currentPath</c>·탐색 히스토리·검색창 접힘 같은 런타임 값과 달리 이건 <b>사용자 설정</b>이라
/// 저장 대상이다. 사용자가 직접 접은 것이므로 다음에 그 워크스페이스를 열 때 그대로 있어야 한다.
/// <para>
/// 슬롯이 없는 v1 파일은 기본값(둘 다 <c>false</c>)으로 읽는다 — 구파일을 거부하지 않는다.
/// </para>
/// </remarks>
public sealed class LeftLayoutSpec
{
    /// <summary>위쪽 폴더 리스트가 접혀 있는가.</summary>
    public bool FoldersCollapsed { get; set; }

    /// <summary>아래쪽 대상함/매크로 탭 영역이 접혀 있는가.</summary>
    public bool LowerCollapsed { get; set; }
}
