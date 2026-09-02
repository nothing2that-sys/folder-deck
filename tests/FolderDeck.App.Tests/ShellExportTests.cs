using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일에서 앱 밖으로 (§4.9 · Phase 5 Stage 2).
/// </summary>
/// <remarks>
/// 여기서 시험하는 것은 <b>드래그에 무엇이 실리는가</b>다. 실제 마우스 제스처와 셸이 그것을
/// 어떻게 푸는지는 흉내 낼 수 없다(<see cref="ShellDropTests"/> 와 같은 경계) — 그쪽은
/// 계측기로 재고 손으로 확인한다.
/// <para>
/// <c>allowedEffects</c> 와 <c>Preferred DropEffect</c> 의 값은 <b>Stage 2 계측이 정했다.</b>
/// 조합을 바꾸면 원본이 사라지는 조합으로 돌아갈 수 있어 여기서 못을 박아 둔다.
/// </para>
/// </remarks>
public sealed class ShellExportTests
{
    private const string Docs = @"C:\deck\docs";
    private const string Work = @"C:\deck\work";

    /// <summary>끌어낸 뭉치 하나 — 파일 둘과 폴더 하나, 출처가 둘로 갈린다(검색 결과, §4.10).</summary>
    private static FileDropPayload Picked() =>
        new(Docs, [
            new OperationItem(Path.Combine(Docs, "spec.md"), false),
            new OperationItem(Path.Combine(Work, "Main.cs"), false),
            new OperationItem(Path.Combine(Work, "Views"), true),
        ]);

    private static DataObject Exported()
    {
        var payload = Picked();
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(payload));
        ShellExport.Attach(data, payload.Items);
        return data;
    }

    // ── 한 드래그에 두 포맷 (§4.9 — 갈림은 시작이 아니라 받는 쪽에서) ─────────

    /// <summary>사설 포맷은 그대로 두고 셸 포맷을 <b>더한다</b> — 갈아치우지 않는다.</summary>
    [Fact]
    public void OneDragCarriesBothTheAppFormatAndTheShellFormat()
    {
        var data = Exported();

        Assert.True(data.GetDataPresent(FolderPanelView.FileDragFormat));
        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
    }

    /// <summary>
    /// 그 드래그가 앱 <b>안</b>에 놓이면 여전히 내부 규칙이다 (§4.9 의 순서 규칙 · §11 C-4).
    /// </summary>
    /// <remarks>
    /// Stage 1 은 두 포맷이 함께 실린 드롭을 <b>손으로 만들 수 없어</b> 흉내 낸 <c>DataObject</c>
    /// 로만 덮었다. 이제 내보내기가 만드는 <b>실물</b>로 같은 것을 확인한다.
    /// </remarks>
    [Fact]
    public void TheExportedDragStillTakesTheInternalRuleWhenItLandsInsideTheApp()
    {
        Assert.Equal(DropRoute.Internal, DropRouter.Route(Exported()));
    }

    // ── 셸 포맷의 내용 ───────────────────────────────────────────────────────

    /// <summary>
    /// 고른 것의 <b>실제 경로</b>가 그대로 간다. 폴더도 경로 하나로 싣고, 서로 다른 폴더에서
    /// 온 것을 출처별로 가르지 않는다 — 검색 결과를 끌면 그렇게 섞인다(§4.10).
    /// </summary>
    [Fact]
    public void TheShellFormatCarriesEveryPickedPathIncludingFoldersFromDifferentParents()
    {
        var paths = Assert.IsType<string[]>(Exported().GetData(DataFormats.FileDrop));

        Assert.Equal(
            [Path.Combine(Docs, "spec.md"), Path.Combine(Work, "Main.cs"), Path.Combine(Work, "Views")],
            paths);
    }

    // ── 원본이 사라지지 않는 근거 (Stage 2 계측) ─────────────────────────────

    /// <summary>
    /// <c>Preferred DropEffect</c> 는 <c>DROPEFFECT_COPY</c>(1) 4바이트다. <b>이것이 셸이 이동을
    /// 기본으로 고르는 것을 막는다</b> — 없으면 같은 볼륨 탐색기가 <c>Move</c> 를 골라 원본이
    /// 사라졌다(Stage 2 실측).
    /// </summary>
    [Fact]
    public void ThePreferredDropEffectSaysCopy()
    {
        var stream = Assert.IsType<MemoryStream>(
            Exported().GetData(ShellExport.PreferredDropEffectFormat));

        Assert.Equal([1, 0, 0, 0], stream.ToArray());
    }

    /// <summary>
    /// 허용은 <c>Copy | Move</c> 다. <b><c>Move</c> 를 빼면 내부 드래그의 이동이 죽는다</b>
    /// (§4.2-2, Phase 3 부터 있던 기능) — 한 번의 <c>DoDragDrop</c> 이 두 규칙을 다 만족해야 한다.
    /// </summary>
    [Fact]
    public void TheAllowedEffectsKeepInternalMoveAlive()
    {
        Assert.Equal(DragDropEffects.Copy | DragDropEffects.Move, ShellExport.AllowedEffects);
    }
}
