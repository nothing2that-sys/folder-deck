using System.Windows.Controls;
using System.Windows.Threading;
using static FolderDeck.App.Tests.StaWpfTestSupport;

namespace FolderDeck.App.Tests;

/// <summary>
/// 상단 ⋯ 메뉴(2026-10-14 S1). 메뉴는 시각 트리 밖이라 DataContext 가 안 이어지면 항목이
/// null 명령에 묶인 채 조용히 죽는다 — 실물 창을 세워 묶인 명령을 직접 본다.
/// </summary>
public sealed class MainWindowMenuTests
{
    [Fact]
    public void TheMoreMenuItemsAreBoundToTheRealCommands()
    {
        RunOnSta(() =>
        {
            using var fx = new MainWindowFixture();
            var window = new MainWindow(fx.ViewModel, new FakeFolderPicker());
            try
            {
                // 트리 밖 요소의 바인딩은 디스패처가 한 바퀴 돌아야 붙는다 — 실제 앱에서는
                // 메뉴를 열기 한참 전에 이미 돈 상태다.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var menu = (ContextMenu)window.FindName("MoreMenu");
                var find = (MenuItem)window.FindName("FindDuplicatesMenuItem");
                var settings = (MenuItem)window.FindName("SettingsMenuItem");
                var launcher = (MenuItem)window.FindName("OpenLauncherMenuItem");

                Assert.Same(fx.ViewModel, menu.DataContext);
                Assert.Same(fx.ViewModel.FindDuplicatesCommand, find.Command);
                Assert.Same(fx.ViewModel.OpenSettingsCommand, settings.Command);
                Assert.Same(fx.ViewModel.OpenLauncherCommand, launcher.Command);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
