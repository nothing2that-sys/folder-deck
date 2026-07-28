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
    bool Confirm);

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
    MacroDraft? Edit(MacroDraft draft, bool canFixPath);
}
