using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;

public sealed class PreviewHandlerHostTests
{
    [Fact]
    public void ActivatingAnUnregisteredClsidFailsGracefullyWithoutThrowing()
    {
        var result = PreviewHandlerHost.TryActivateOutOfProcess(Guid.NewGuid());

        Assert.Null(result);
    }
}
