using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

public partial class TileGridView : UserControl
{

    public const string FolderRowFormat = "FolderDeck.FolderRow";

    private DragState? _drag;

    public TileGridView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private int Cols => ViewModel?.GridCols ?? 4;

    private int Rows => ViewModel?.GridRows ?? 4;

    private sealed class DragState
    {
        public required TileViewModel Tile { get; init; }

        public required bool IsResize { get; init; }

        public int GrabOffsetX { get; init; }

        public int GrabOffsetY { get; init; }

        public int TargetX { get; set; }

        public int TargetY { get; set; }

        public int TargetSpanX { get; set; }

        public int TargetSpanY { get; set; }

        public bool IsValid { get; set; }
    }

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsEditingLayout: true }
            || sender is not FrameworkElement { DataContext: TileViewModel tile })
        {
            return;
        }

        var (cellX, cellY) = CellAt(e.GetPosition(Host));

        BeginDrag(new DragState
        {
            Tile = tile,
            IsResize = false,
            GrabOffsetX = Math.Clamp(cellX - tile.CellX, 0, tile.SpanX - 1),
            GrabOffsetY = Math.Clamp(cellY - tile.CellY, 0, tile.SpanY - 1),
        });

        e.Handled = true;
    }

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsEditingLayout: true }
            || sender is not FrameworkElement { DataContext: TileViewModel tile })
        {
            return;
        }

        BeginDrag(new DragState { Tile = tile, IsResize = true });

        e.Handled = true;
    }

    private void BeginDrag(DragState drag)
    {
        _drag = drag;
        drag.Tile.IsDragging = true;

        Host.Focus();
        Host.CaptureMouse();
        UpdatePreview(Mouse.GetPosition(Host));
    }

    private void OnHostMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not null)
        {
            UpdatePreview(e.GetPosition(Host));
        }
    }

    private void OnHostMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } drag || ViewModel is not { } vm)
        {
            return;
        }

        EndDrag();
        e.Handled = true;

        if (!drag.IsValid)
        {

            vm.ReportTileOverlap();
            return;
        }

        if (drag.IsResize)
        {
            vm.ResizeTile(drag.Tile, drag.TargetSpanX, drag.TargetSpanY);
        }
        else
        {
            vm.MoveTile(drag.Tile, drag.TargetX, drag.TargetY);
        }
    }

    private void OnHostLostCapture(object sender, MouseEventArgs e) => EndDrag();

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is not null)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    private void EndDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        drag.Tile.IsDragging = false;
        PreviewLayer.Visibility = Visibility.Collapsed;

        if (Host.IsMouseCaptured)
        {
            Host.ReleaseMouseCapture();
        }
    }

    private void UpdatePreview(Point position)
    {
        if (_drag is not { } drag || ViewModel is not { } vm)
        {
            return;
        }

        var (cellX, cellY) = CellAt(position);
        var tile = drag.Tile;

        if (drag.IsResize)
        {
            drag.TargetX = tile.CellX;
            drag.TargetY = tile.CellY;
            drag.TargetSpanX = Math.Clamp(cellX - tile.CellX + 1, 1, Cols - tile.CellX);
            drag.TargetSpanY = Math.Clamp(cellY - tile.CellY + 1, 1, Rows - tile.CellY);
        }
        else
        {
            drag.TargetSpanX = tile.SpanX;
            drag.TargetSpanY = tile.SpanY;
            drag.TargetX = Math.Clamp(cellX - drag.GrabOffsetX, 0, Cols - tile.SpanX);
            drag.TargetY = Math.Clamp(cellY - drag.GrabOffsetY, 0, Rows - tile.SpanY);
        }

        drag.IsValid = vm.CanPlaceTile(tile, drag.TargetX, drag.TargetY, drag.TargetSpanX, drag.TargetSpanY);

        Grid.SetColumn(PreviewBox, drag.TargetX);
        Grid.SetRow(PreviewBox, drag.TargetY);
        Grid.SetColumnSpan(PreviewBox, drag.TargetSpanX);
        Grid.SetRowSpan(PreviewBox, drag.TargetSpanY);

        PreviewBox.BorderBrush = (Brush)FindResource(drag.IsValid ? "Brush.Accent" : "Brush.Danger");
        PreviewBox.Background = (Brush)FindResource(
            drag.IsValid ? "Brush.AccentSurface" : "Brush.DropInvalidSurface");
        PreviewText.Text = drag.IsValid
            ? $"{drag.TargetSpanX}×{drag.TargetSpanY}"
            : "겹침 — 놓을 수 없다";

        PreviewLayer.Visibility = Visibility.Visible;
    }

    private (int X, int Y) CellAt(Point position)
    {
        var cellWidth = Host.ActualWidth / Cols;
        var cellHeight = Host.ActualHeight / Rows;

        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return (0, 0);
        }

        return (
            Math.Clamp((int)(position.X / cellWidth), 0, Cols - 1),
            Math.Clamp((int)(position.Y / cellHeight), 0, Rows - 1));
    }

    private void OnCellClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsEditingLayout: true } vm
            || sender is not FrameworkElement { DataContext: GridCellViewModel cell })
        {
            return;
        }

        FolderPicker.PlacementTarget = (UIElement)sender;
        vm.BeginPlaceInCell(cell);
        e.Handled = true;
    }

    private void OnFolderPickerClosed(object? sender, EventArgs e) => ViewModel?.CancelPlace();

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TargetCell(sender, e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnCellDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GridCellViewModel cell })
        {
            cell.IsDropTarget = false;
        }
    }

    private async void OnCellDrop(object sender, DragEventArgs e)
    {
        var target = TargetCell(sender, e);

        if (sender is FrameworkElement { DataContext: GridCellViewModel cell })
        {
            cell.IsDropTarget = false;
        }

        if (target is not { } drop || ViewModel is not { } vm)
        {
            return;
        }

        e.Handled = true;

        await vm.PlaceFolderInCellAsync(drop.Row, drop.Cell);
    }

    private (FolderRowViewModel Row, GridCellViewModel Cell)? TargetCell(object sender, DragEventArgs e)
    {
        if (ViewModel is not { IsEditingLayout: true }
            || sender is not FrameworkElement { DataContext: GridCellViewModel cell }
            || !cell.IsFree
            || !e.Data.GetDataPresent(FolderRowFormat)
            || e.Data.GetData(FolderRowFormat) is not FolderRowViewModel { CanPlaceInGrid: true } row)
        {
            return null;
        }

        cell.IsDropTarget = true;
        return (row, cell);
    }

    private void OnToggleKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TileViewModel tile })
        {
            ViewModel?.ToggleTileKindCommand.Execute(tile);
        }
    }

    private void OnRemoveTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TileViewModel tile })
        {
            ViewModel?.RemoveTileCommand.Execute(tile);
        }
    }
}
