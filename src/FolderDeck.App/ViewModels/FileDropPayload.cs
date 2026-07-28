using System.Collections.Generic;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 그리드 타일에서 끌어낸 한 뭉치 (§4.2-2 · Phase 3 Stage 3).
/// </summary>
/// <remarks>
/// <see cref="SourceFolder"/> 를 같이 들고 다니는 이유: 같은 폴더 안으로 놓는 드롭을
/// <b>과녁 단계에서</b> 걸러내기 위해서다. 항목 경로에서 부모를 매번 계산할 수도 있지만,
/// 뭉치의 출처는 패널 하나(§4.8 — 선택은 타일 하나에만)라서 한 번만 담아 두면 된다.
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
