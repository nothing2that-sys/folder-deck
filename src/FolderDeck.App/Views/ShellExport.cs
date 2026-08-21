using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Views;

/// <summary>
/// 타일에서 앱 밖으로 (§4.9 · Phase 5 Stage 2) — <b>끄는 쪽이 싣는 것.</b>
/// </summary>
/// <remarks>
/// 시작 제스처는 갈리지 않는다(§4.9). 내부 드래그도 이미 <c>DoDragDrop</c> 이고, "내부"와 "셸"의
/// 차이는 <see cref="DataObject"/> 에 실은 포맷뿐이다 — 그래서 여기서 하는 일은 <b>포맷 둘을
/// 더 싣는 것</b>이 전부다. 갈림은 <see cref="DropRouter"/> 가 있는 받는 쪽에서 일어난다.
/// <para>
/// 복사는 <b>셸이 한다.</b> 엔진(<c>IFileOperationEngine</c>)은 이 경로에 관여하지 않는다.
/// </para>
/// </remarks>
public static class ShellExport
{
    /// <summary>
    /// <c>CFSTR_PREFERREDDROPEFFECT</c> — 받는 쪽이 기본 동작을 고를 때 보는 포맷.
    /// </summary>
    public const string PreferredDropEffectFormat = "Preferred DropEffect";

    /// <summary><c>DROPEFFECT_COPY</c>. 이 값 하나를 4바이트로 싣는다.</summary>
    private const uint DropEffectCopy = 1;

    /// <summary>
    /// <c>DROPEFFECT_MOVE</c> — 탐색기가 <c>Ctrl+X</c> 로 싣는 값과 같다 (D36).
    /// </summary>
    private const uint DropEffectMove = 2;

    /// <summary>
    /// <c>DoDragDrop</c> 의 셋째 인자. <b>한 번의 호출이 두 규칙을 다 만족해야 한다</b> —
    /// 내부 드래그는 기본 이동/<c>Ctrl</c> 복사라 <see cref="DragDropEffects.Move"/> 가 허용에
    /// 있어야 하고(§4.2-2), 셸 내보내기는 기본이 복사여야 한다(§4.9).
    /// </summary>
    /// <remarks>
    /// <b>Stage 2 계측 결과가 이 값을 정했다.</b> 같은 볼륨·탐색기에서:
    /// <list type="bullet">
    /// <item><c>Copy</c> 하나만 주면 안전하지만 <b>내부 이동이 죽는다</b>(Phase 3 회귀).</item>
    /// <item><c>Copy | Move</c> 만 주면 탐색기가 <c>Move</c> 를 골라 <b>원본이 사라졌다</b>(실측 7건).</item>
    /// <item><c>Copy | Move</c> + <see cref="PreferredDropEffectFormat"/>=COPY 는 <b>복사로
    /// 떨어지고 원본이 남았다</b>(같은 볼륨·다른 볼륨 모두 실측).</item>
    /// </list>
    /// <c>Shift</c> 를 누르면 셸이 이동을 고르고 원본이 사라진다 — <b>막지 않는다.</b> 이 앱은
    /// 탐색기의 덱이고 표준 제스처를 가로채지 않는다(JH 결정, Stage 2).
    /// </remarks>
    public const DragDropEffects AllowedEffects = DragDropEffects.Copy | DragDropEffects.Move;

    /// <summary>
    /// 사설 포맷이 이미 실린 <paramref name="data"/> 에 <b>셸 포맷을 더한다.</b>
    /// </summary>
    /// <remarks>
    /// <paramref name="items"/> 는 <b>출처가 하나라는 전제가 없다</b> — 검색 결과를 끌면 서로
    /// 다른 폴더의 경로가 섞여 들어온다(§4.10). 셸은 그것을 그대로 받으므로 여기서 출처별로
    /// 가르거나 걸러내지 않는다. 폴더도 경로 하나로 실어 보낸다 — 폴더를 폴더로 풀지, 그 안의
    /// 파일로 풀지는 <b>받는 쪽이 정한다</b>(§4.9).
    /// <para>
    /// <b>기본값이 복사다 — 이동은 부르는 쪽이 명시해야 한다</b>(D36). 파괴적인 방향만 명시를
    /// 요구하는 꼴이라 빠뜨리면 안전한 쪽으로 떨어진다. 드래그 내보내기(§4.9)는 기본값을
    /// 그대로 쓰므로 <c>PDE = DROPEFFECT_COPY</c> 라는 §4.9 의 약속이 그대로 지켜지고,
    /// <b>싣는 함수는 여전히 하나다</b>(§4.6 — 「드래그 내보내기와 같은 함수로 싣는다」).
    /// </para>
    /// </remarks>
    /// <param name="move">
    /// <c>true</c> 면 <c>DROPEFFECT_MOVE</c>(2) 를 싣는다 — <c>Ctrl+X</c> 가 쓰는 자리다.
    /// 기본값 <c>false</c> 는 <c>DROPEFFECT_COPY</c>(1) 다.
    /// </param>
    public static void Attach(DataObject data, IReadOnlyList<OperationItem> items, bool move = false)
    {
        data.SetData(DataFormats.FileDrop, items.Select(i => i.Path).ToArray(), autoConvert: true);

        // 스트림 하나에 DWORD 하나. DataObject 가 소유권을 가져가므로 여기서 닫지 않는다.
        data.SetData(
            PreferredDropEffectFormat,
            new MemoryStream(BitConverter.GetBytes(move ? DropEffectMove : DropEffectCopy)));
    }
}
