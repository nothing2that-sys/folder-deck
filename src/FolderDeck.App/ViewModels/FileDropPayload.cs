using System.Collections.Generic;
using System.Threading.Tasks;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 그리드 타일에서 끌어낸 한 뭉치 (§4.2-2 · Phase 3 Stage 3).
/// </summary>
/// <remarks>
/// <see cref="SourceFolder"/> 는 <b>더 이상 과녁 판정에 쓰이지 않는다</b>(Phase 5 Stage 1).
/// ①(온 곳과 같은 폴더)의 판정 단위가 뭉치에서 <b>항목</b>으로 바뀌었고(§4.9), 출처는
/// 항목 경로의 부모에서 얻는다 — 셸 드롭에는 "뭉치의 출처가 하나"라는 전제가 없어서
/// 두 진입점이 같은 함수를 쓰려면 항목마다 봐야 했다. 뭉치 자체가 어디서 왔는지는 여전히
/// 사실이라 남겨 둔다(끌어낸 자리를 말해야 하는 문구·진단이 이걸 읽는다).
/// </remarks>
public sealed record FileDropPayload(string SourceFolder, IReadOnlyList<OperationItem> Items);

/// <summary>
/// 파일을 놓을 수 있는 자리 — 왼쪽 폴더 행과 대상함 항목이 같은 과녁이다 (§4.2-2).
/// </summary>
/// <remarks>
/// 둘은 서로 다른 뷰모델이지만 드롭 처리는 똑같다(목적지 폴더 하나 + 과녁 강조). 뷰가
/// 타입을 나눠 두 벌로 다루지 않도록 공통 계약을 둔다.
/// </remarks>
public interface IFileDropTarget
{
    FolderEntry Entry { get; }

    /// <summary>드래그가 이 자리 위에 있는가 (파란 과녁 강조 — Phase 2 빈 칸과 같은 결).</summary>
    bool IsDropTarget { get; set; }

    /// <summary>지금 놓으면 복사인가 — 과녁 글귀가 이것을 읽는다 (§4.2-2).</summary>
    /// <remarks>
    /// <b><see cref="IsDropTarget"/> 과 같은 자리에서 같이 정해진다.</b> 판정을 낸 곳이 문구도
    /// 정해야 <c>DragOver</c> 가 말한 것과 <c>Drop</c> 이 한 것이 갈리지 않는다.
    /// </remarks>
    bool DropTargetCopies { get; set; }
}

/// <summary>
/// 과녁에 뜨는 글귀 — <b>놓으면 무엇이 되는지</b>를 말한다 (§4.2-2 259행).
/// </summary>
/// <remarks>
/// 규칙이 조건부가 된 이상(같은 볼륨이면 이동 · 다른 볼륨이면 복사) 손동작만으로는 무엇이 될지
/// 알 수 없다. 그런데 이 경로에는 확인창을 두지 않기로 했으므로(§4.2-2) <b>놓기 전에 말하는</b>
/// 자리는 과녁뿐이다 — §3.1 배지 · §4.3 상시 표시와 같은 결이다.
/// <para>
/// <b>글귀는 여기 한 곳에서만 만든다.</b> 받는 자리 셋(왼쪽 폴더 행 · 대상함 · 타일)이 같은 값을
/// 읽어야 하고, XAML 은 뷰모델이 낸 것을 <b>읽기만 한다</b> — 글자를 XAML 에 박으면 세 벌이 되고
/// 하나만 고쳐진다.
/// </para>
/// </remarks>
public static class DropTargetCaption
{
    public static string For(bool copy) => copy ? "여기로 복사" : "여기로 이동";
}

/// <summary>
/// 셸에서 끌어온 것을 받아 줄 수 있는 쪽 (§4.9 · Phase 5 Stage 1).
/// </summary>
/// <remarks>
/// <see cref="IFileDropTarget"/> 과 달리 <b>경로로 말한다</b> — 셸 드롭이 타일에 떨어지면
/// 목적지는 타일의 <c>CurrentPath</c> 이고 그 자리에는 <see cref="FolderEntry"/> 가 없다
/// (§4.9 — 앵커가 아니라 보고 있는 경로가 목적지다).
/// <para>
/// 파일 목록 패널은 엔진도, 다른 조작이 도는지도 모른다. 판정과 실행은 그것을 아는
/// <c>MainViewModel</c> 것이고 패널은 통로만 들고 있다 — §4.4 의
/// <see cref="FolderPanelViewModel.TrashSelectionCommand"/> 와 같은 결이다.
/// </para>
/// </remarks>
public interface IShellDropHost
{
    /// <summary>이 목적지가 이 항목들을 받는가 — ①②③ · 배치 편집 중 (§4.9).</summary>
    bool CanDropOnto(string? destinationPath, IReadOnlyList<OperationItem>? items);

    /// <summary>받는다. 복사냐 이동이냐는 부르는 쪽이 이미 정했다.</summary>
    Task DropOntoPathAsync(string destinationPath, IReadOnlyList<OperationItem> items, bool copy);
}
