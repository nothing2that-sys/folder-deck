using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class PanelFocusPolicyTests
{
    [Fact]
    public void AButtonChildKeepsTheFirstClick()
    {
        RunOnSta(() =>
        {
            var content = new TextBlock();
            _ = new Button { Content = content };

            Assert.False(PanelFocusPolicy.ShouldFocusPanelOnLeftClick(content));
        });
    }

    [Fact]
    public void AnEmptySurfaceStillFocusesThePanel()
    {
        RunOnSta(() =>
        {
            var surface = new Border();

            Assert.True(PanelFocusPolicy.ShouldFocusPanelOnLeftClick(surface));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
