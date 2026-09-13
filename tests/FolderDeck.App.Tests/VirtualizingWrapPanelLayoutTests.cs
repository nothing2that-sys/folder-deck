using FolderDeck.App.Views;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FolderDeck.App.Tests;

/// <summary>
/// 큰 아이콘 뷰 패널의 순수 격자 계산(Phase 21 · D124).
/// WPF 컨트롤은 MTA 테스트에서 만들지 않고 열·가시 범위·인덱스 변환만 검증한다.
/// </summary>
public sealed class VirtualizingWrapPanelLayoutTests
{
    [Theory]
    [InlineData(119, 1)]
    [InlineData(120, 1)]
    [InlineData(239, 1)]
    [InlineData(240, 2)]
    [InlineData(800, 6)]
    public void ColumnCountUsesOnlyWholeFixedWidthCells(double width, int expected)
    {
        Assert.Equal(expected, VirtualizingWrapLayout.ColumnCount(width, itemWidth: 120));
    }

    [Fact]
    public void InitialViewportRealizesOnlyFiveRows()
    {
        var range = VirtualizingWrapLayout.VisibleRange(
            itemCount: 3_000, columns: 6, verticalOffset: 0, viewportHeight: 600, itemHeight: 120);

        Assert.Equal(new GridIndexRange(0, 29), range);
        Assert.Equal(30, range.Count);
    }

    [Theory]
    [InlineData(3_000, 247.5, 1_482, 1_517)]
    [InlineData(30_000, 2_497.5, 14_982, 15_017)]
    public void FractionalMiddleOffsetIncludesBothPartialRows(
        int itemCount, double offset, int expectedFirst, int expectedLast)
    {
        var range = VirtualizingWrapLayout.VisibleRange(
            itemCount, columns: 6, offset, viewportHeight: 600, itemHeight: 120);

        Assert.Equal(new GridIndexRange(expectedFirst, expectedLast), range);
        Assert.Equal(36, range.Count);
    }

    [Fact]
    public void EmptyItemsHaveNoVisibleIndex()
    {
        var range = VirtualizingWrapLayout.VisibleRange(
            itemCount: 0, columns: 6, verticalOffset: 0, viewportHeight: 600, itemHeight: 120);

        Assert.True(range.IsEmpty);
        Assert.Equal(0, range.Count);
    }

    [Fact]
    public void IndexAndCellConversionsAreBidirectional()
    {
        var cell = VirtualizingWrapLayout.CellFromIndex(index: 37, columns: 6);

        Assert.Equal(new GridCell(6, 1), cell);
        Assert.Equal(37, VirtualizingWrapLayout.IndexFromCell(cell.Row, cell.Column, columns: 6));
    }

    [Theory]
    [InlineData(3, 5, 5, 3)]
    [InlineData(7, 5, 5, 5)]
    [InlineData(10, 5, 5, 6)]
    public void RevealOffsetMovesOnlyWhenTheRowIsOutsideTheViewport(
        int row, double currentOffset, double viewportRows, double expected)
    {
        Assert.Equal(expected, VirtualizingWrapLayout.OffsetToRevealRow(row, currentOffset, viewportRows));
    }

    [Fact]
    public void ACollapsedRecyclingListRealizesFilesAfterItBecomesVisibleAndResets()
    {
        RunOnSta(() =>
        {
            var list = new ListView
            {
                Width = 360,
                Height = 240,
                Visibility = Visibility.Collapsed,
                ItemsSource = new[] { "folder-a", "folder-b" },
                ItemsPanel = new ItemsPanelTemplate(
                    new FrameworkElementFactory(typeof(VirtualizingWrapPanel))),
            };

            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true);

            using var host = new TestWindow(list);
            host.Show();
            list.Visibility = Visibility.Visible;
            host.UpdateLayout();

            list.Visibility = Visibility.Collapsed;
            list.ItemsSource = new[] { "folder-a", "file-a.dwg", "file-b.png" };
            list.Visibility = Visibility.Visible;
            host.UpdateLayout();

            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(2));
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

    private sealed class TestWindow : IDisposable
    {
        private readonly Window _window;

        public TestWindow(UIElement content)
        {
            _window = new Window
            {
                Width = 400,
                Height = 300,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = content,
            };
        }

        public void Show() => _window.Show();

        public void UpdateLayout() => _window.UpdateLayout();

        public void Dispose() => _window.Close();
    }
}
