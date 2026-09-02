using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 사설 드래그 포맷을 글자로 싣는 코덱 (§4.9 · Phase 14 D77-b).
/// </summary>
/// <remarks>
/// 앞의 넷은 코덱 자체의 왕복·거절을 본다. 뒤의 둘(<see cref="APackedStringCrossesTheBoundaryAsAStringNotAnObject"/>
/// · <see cref="APackedStringWithTheShellFormatAttachedStillTakesTheInternalRule"/>)이 이 회차의
/// 핵심이다 — 실제 크로스프로세스는 흉내 낼 수 없지만(손 확인이 본다), <c>DataObject</c> 에
/// <b>문자열만</b> 실어 <see cref="DropRouter.Resolve"/> 에 넘기는 것으로 「객체가 아니라 글자가
/// 오간다」는 경계를 흉내 낸다.
/// </remarks>
public sealed class DragPayloadCodecTests
{
    // ── 코덱 자체 (§6-1 시험 1~4) ─────────────────────────────────────────────

    [Fact]
    public void PackThenUnpackRoundTripsMixedItemsInOrder()
    {
        var payload = new FileDropPayload(@"C:\deck\work", [
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
            new OperationItem(@"C:\deck\work\Recipe\Recipe.cs", false),
        ]);

        var unpacked = DragPayloadCodec.Unpack(DragPayloadCodec.Pack(payload));

        Assert.NotNull(unpacked);
        Assert.Equal(payload.SourceFolder, unpacked.SourceFolder);
        Assert.Equal(payload.Items, unpacked.Items);
    }

    [Fact]
    public void AnEmptyBundleRoundTrips()
    {
        var payload = new FileDropPayload(@"C:\deck\work", []);

        var unpacked = DragPayloadCodec.Unpack(DragPayloadCodec.Pack(payload));

        Assert.NotNull(unpacked);
        Assert.Empty(unpacked.Items);
    }

    [Fact]
    public void TextThatIsNotJsonIsRejectedWithoutThrowing()
    {
        Assert.Null(DragPayloadCodec.Unpack("뭉치가 아니다"));
    }

    [Fact]
    public void JsonWithTheWrongShapeIsRejected()
    {
        Assert.Null(DragPayloadCodec.Unpack("""{"foo":1,"bar":"baz"}"""));
    }

    // ── 경계 흉내 (§6-1 시험 5~6 — 이 회차의 핵심) ────────────────────────────

    /// <summary>
    /// <c>DataObject</c> 에 <c>Pack</c> 결과 <b>문자열</b>만 실으면 <c>Resolve</c> 가 그것을 풀어
    /// 항목을 그대로 낸다 — 객체를 실은 적이 없다.
    /// </summary>
    [Fact]
    public void APackedStringCrossesTheBoundaryAsAStringNotAnObject()
    {
        var payload = new FileDropPayload(@"C:\deck\work", [
            new OperationItem(@"C:\deck\work\Main.cs", false),
        ]);

        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(payload));

        Assert.IsType<string>(data.GetData(FolderPanelView.FileDragFormat));

        var resolved = DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out");

        Assert.NotNull(resolved);
        Assert.Equal(payload.Items, resolved.Value.Items);
    }

    /// <summary>
    /// 셸 포맷이 함께 실려 있어도 문자열로 실린 사설 포맷이 이긴다 — 내부 규칙 그대로다
    /// (§4.9 「받는 쪽의 순서」).
    /// </summary>
    [Fact]
    public void APackedStringWithTheShellFormatAttachedStillTakesTheInternalRule()
    {
        var payload = new FileDropPayload(@"C:\deck\work", [
            new OperationItem(@"C:\deck\work\Main.cs", false),
        ]);

        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(payload));
        data.SetData(DataFormats.FileDrop, new[] { @"C:\deck\work\Main.cs" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));

        var resolved = DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out");

        Assert.NotNull(resolved);
        Assert.Equal(payload.Items, resolved.Value.Items);
    }
}
