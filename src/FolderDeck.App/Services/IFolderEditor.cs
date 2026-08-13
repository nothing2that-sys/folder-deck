namespace FolderDeck.App.Services;

/// <summary>
/// 폴더 등록 한 벌의 편집 결과 (§4.11-3). <b>고치는 것은 이 둘뿐이다.</b>
/// </summary>
/// <remarks>
/// <c>pinned</c> 는 여기에 없다(D2) — 상시냐 순환이냐는 타일 상태에서 유도되므로 폴더 쪽에서도
/// 고칠 수 있게 하면 같은 사실이 두 곳에 생기고, 어긋날 때 어느 쪽이 옳은지 정할 근거가 없다.
/// <c>viewMode</c>·<c>sortBy</c>·<c>sortDesc</c>·<c>rotationSlot</c> 도 각자의 기존 진입점을 쓴다.
/// </remarks>
/// <param name="DisplayName">왼쪽 리스트에 보일 이름. 비우면 잎 이름으로 되돌아간다.</param>
/// <param name="Description">설명. 비어 있으면 표시 자리가 통째로 숨는다.</param>
public sealed record FolderEditDraft(string? DisplayName, string? Description);

/// <summary>
/// 폴더 등록 수정 창 (§4.11-3). 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface IFolderEditor
{
    /// <summary>창을 띄우고 결과를 돌려준다. <b>취소면 null</b>.</summary>
    /// <param name="draft">지금 값으로 미리 채운 것.</param>
    /// <param name="path">고치는 폴더의 앵커 경로 — 어느 폴더를 고치는지 보여 주기만 한다.</param>
    FolderEditDraft? Edit(FolderEditDraft draft, string path);
}
