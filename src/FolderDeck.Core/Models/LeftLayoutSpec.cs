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

    /// <summary>
    /// 왼쪽 컬럼이 <b>통째로</b> 접혀 있는가 (schemaVersion 6 · Phase 8-A ⑦ → 8-E ①).
    /// </summary>
    /// <remarks>
    /// 위 둘은 이 컬럼 <b>안</b>의 두 칸을 접고, 이것은 컬럼 자체를 없애 그리드가 창을 다 쓰게
    /// 한다. 셋 다 사용자가 손으로 접은 것이라 성격이 같은데 <b>바깥 하나만 저장되지 않았다</b> —
    /// 8-A 가 저장 스키마를 열지 않기로 해서 남은 자리이고, 여기서 같은 칸에 넣는다.
    /// <para>
    /// 키가 없는 구파일은 <c>false</c>(펼침)로 읽힌다 — 8-E 전의 동작 그대로다(창을 열면
    /// 늘 펴져 있었다). 안쪽 두 값과 독립이라, 통째로 접었다 펴면 접기 전 모습이 그대로 돌아온다.
    /// </para>
    /// </remarks>
    public bool LeftPanelCollapsed { get; set; }
}
