using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

/// <summary>
/// 오른쪽 4x4 그리드 (§3.2 · Phase 2) — 타일 렌더링과 배치 편집 제스처.
/// </summary>
/// <remarks>
/// 규칙 판정(격자 밖·겹침)은 전부 <see cref="MainViewModel"/> → Core 의 <c>GridLayout</c> 이
/// 한다. 여기서는 픽셀을 셀로 바꾸고, 미리보기를 그리고, 놓는 순간 뷰모델에 넘길 뿐이다.
/// </remarks>
public partial class TileGridView : UserControl
{
    /// <summary>왼쪽 폴더 리스트 → 빈 칸 드래그드롭에 쓰는 앱 내부 포맷.</summary>
    public const string FolderRowFormat = "FolderDeck.FolderRow";

    private DragState? _drag;

    public TileGridView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private int Cols => ViewModel?.GridCols ?? 4;

    private int Rows => ViewModel?.GridRows ?? 4;

    /// <summary>진행 중인 이동/리사이즈 한 건.</summary>
    private sealed class DragState
    {
        public required TileViewModel Tile { get; init; }

        /// <summary>모서리 그립을 잡았는가(아니면 이동).</summary>
        public required bool IsResize { get; init; }

        /// <summary>타일 왼쪽 위에서 몇 칸 안쪽을 잡았는지 — 이동할 때 손이 미끄러지지 않게.</summary>
        public int GrabOffsetX { get; init; }

        public int GrabOffsetY { get; init; }

        public int TargetX { get; set; }

        public int TargetY { get; set; }

        public int TargetSpanX { get; set; }

        public int TargetSpanY { get; set; }

        public bool IsValid { get; set; }
    }

    // ── 타일 이동 · 리사이즈 ─────────────────────────────────────────────────

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

        // 이동 핸들러(부모 오버레이)까지 올라가면 두 제스처가 겹친다.
        e.Handled = true;
    }

    private void BeginDrag(DragState drag)
    {
        _drag = drag;
        drag.Tile.IsDragging = true;

        // Esc 취소를 받으려면 키보드 포커스가 여기 있어야 한다.
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
            // 겹치면 거부한다 — 밀어내지 않는다. 왜 안 됐는지는 말해 준다.
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

        // 먼저 비운다 — ReleaseMouseCapture 가 LostMouseCapture 로 여기 다시 들어온다.
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

    /// <summary>픽셀 → 셀. 세 층이 같은 4x4 격자라 Host 크기만으로 환산된다.</summary>
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

    // ── 빈 칸: 클릭으로 넣기 ─────────────────────────────────────────────────

    private void OnCellClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsEditingLayout: true } vm
            || sender is not FrameworkElement { DataContext: GridCellViewModel cell })
        {
            return;
        }

        // IsOpen 이 켜지기 전에 붙일 자리를 정해야 팝업이 그 칸 아래에 뜬다.
        FolderPicker.PlacementTarget = (UIElement)sender;
        vm.BeginPlaceInCell(cell);
        e.Handled = true;
    }

    private void OnFolderPickerClosed(object? sender, EventArgs e) => ViewModel?.CancelPlace();

    // ── 빈 칸: 왼쪽 리스트에서 끌어다 놓기 ───────────────────────────────────

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

        // 넣은 폴더의 내용을 바로 읽는다. 열거 실패는 패널이 상태로 표시하므로
        // 여기까지 예외로 올라오지 않는다(§5).
        await vm.PlaceFolderInCellAsync(drop.Row, drop.Cell);
    }

    /// <summary>드롭을 받을 수 있는 조합이면 (폴더 행, 칸). 아니면 null. 과녁 강조도 여기서.</summary>
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

    // ── 타일 우클릭 ──────────────────────────────────────────────────────────

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
