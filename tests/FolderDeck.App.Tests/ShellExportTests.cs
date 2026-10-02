using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;













public sealed class ShellExportTests
{
    private const string Docs = @"C:\deck\docs";
    private const string Work = @"C:\deck\work";


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




    [Fact]
    public void OneDragCarriesBothTheAppFormatAndTheShellFormat()
    {
        var data = Exported();

        Assert.True(data.GetDataPresent(FolderPanelView.FileDragFormat));
        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
    }








    [Fact]
    public void TheExportedDragStillTakesTheInternalRuleWhenItLandsInsideTheApp()
    {
        Assert.Equal(DropRoute.Internal, DropRouter.Route(Exported()));
    }







    [Fact]
    public void TheShellFormatCarriesEveryPickedPathIncludingFoldersFromDifferentParents()
    {
        var paths = Assert.IsType<string[]>(Exported().GetData(DataFormats.FileDrop));

        Assert.Equal(
            [Path.Combine(Docs, "spec.md"), Path.Combine(Work, "Main.cs"), Path.Combine(Work, "Views")],
            paths);
    }








    [Fact]
    public void ThePreferredDropEffectSaysCopy()
    {
        var stream = Assert.IsType<MemoryStream>(
            Exported().GetData(ShellExport.PreferredDropEffectFormat));

        Assert.Equal([1, 0, 0, 0], stream.ToArray());
    }





    [Fact]
    public void TheAllowedEffectsKeepInternalMoveAlive()
    {
        Assert.Equal(DragDropEffects.Copy | DragDropEffects.Move, ShellExport.AllowedEffects);
    }
}
