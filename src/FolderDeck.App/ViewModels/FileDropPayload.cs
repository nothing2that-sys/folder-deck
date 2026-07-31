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
