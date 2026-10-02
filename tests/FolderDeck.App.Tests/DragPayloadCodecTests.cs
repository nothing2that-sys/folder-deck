using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;











public sealed class DragPayloadCodecTests
{


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
