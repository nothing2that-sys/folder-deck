using FolderDeck.Core.Models;

namespace FolderDeck.App.Services;

/// <summary>
/// 매크로 한 벌의 편집 결과 (§4.7). 화면에서 고른 것을 그대로 담는다 —
/// <c>folderIds</c> 처럼 <b>지금 상태에서 굳혀야</b> 하는 값은 뷰모델이 채운다.
/// </summary>
public sealed record MacroDraft(
    string Name,
    FileOperationKind Op,
    MacroSourceKind SourceKind,
    string? FixedPath,
    MacroDestKind DestKind,
    ConflictPolicy OnConflict,
    bool Confirm,

    // 고치기 전용(코덱스 적대적 검증 발견, 2026-09-18) — 둘 다 기본은 꺼짐(=예전 값 유지).
    // 고정 경로·고정 폴더는 이미 굳어 있으면 지금 선택/체크가 달라도 조용히 새지 않아야
    // 한다(§ EditingWithoutTouchingDestKeepsThePinnedFolders). 그런데 그렇게만 하면 사용자가
    // "진짜로 다시 고정하고 싶다"는 뜻을 전할 방법이 없어졌다 — 이 두 플래그가 그 뜻이다.
    bool RetargetFixedPath = false,
    bool RepinFolderIds = false);

/// <summary>
/// 매크로 만들기·고치기 창 (§4.7). 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface IMacroEditor
{
    /// <summary>
    /// 창을 띄우고 결과를 돌려준다. 취소면 null.
    /// </summary>
    /// <param name="draft">
    /// 지금 화면 상태에서 미리 채운 값. 대개는 이름만 쓰고 확인을 누르게 된다 —
    /// 방금 손으로 한 조작을 규칙으로 굳히는 것이 이 기능의 주 용도라서.
    /// </param>
    /// <param name="canFixPath">
    /// "이 경로로 고정"을 고를 수 있는가. 대상이 <b>하나</b>일 때만 가능하다 —
    /// <c>source.path</c> 는 §4.7 에서 경로 하나다.
    /// </param>
    /// <param name="isEdit">
    /// 있는 규칙을 고치는 것인가(만드는 것이 아니라). 창 제목·버튼 글씨와, 나머지 축을
    /// 접어 둘지(만들기는 접음, 고치기는 지금 값을 보여야 하니 편다)를 가른다.
    /// </param>
    MacroDraft? Edit(MacroDraft draft, bool canFixPath, bool isEdit);
}
