using System.Runtime.ExceptionServices;
using System.Windows;

namespace FolderDeck.App.Tests;

internal static class StaWpfTestSupport
{
    private static readonly object AppSetupLock = new();

    public static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureWpfApplicationResources();
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

    private static void EnsureWpfApplicationResources()
    {
        lock (AppSetupLock)
        {
            if (Application.Current is not null)
            {
                return;
            }

            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/FolderDeck;component/Themes/LightTheme.xaml"),
            });
        }
    }
}
