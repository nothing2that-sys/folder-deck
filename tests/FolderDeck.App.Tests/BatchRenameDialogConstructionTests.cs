using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class BatchRenameDialogConstructionTests
{
    [Fact]
    public void ConstructingWithOneOrMoreNamesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new BatchRenameDialog(["a.txt", "b.txt"], []);
        });
    }

    [Fact]
    public void ConstructingWithASingleNameDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new BatchRenameDialog(["only.txt"], []);
        });
    }
}
