using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Views;

public partial class FolderPanelView : UserControl
{

    public const string FileDragFormat = "FolderDeck.FileDrag";

    private Point? _dragOrigin;

    private FileItemViewModel? _dragPressedItem;

    private FileItemViewModel? _pendingSingleSelect;

    private ListView? _pendingSingleSelectList;

    private readonly ShellThumbnailService _previewThumbnailService = new();

    private readonly ConditionalWeakTable<Image, LargeIconRequestState> _largeIconRequests = new();

    private sealed class LargeIconRequestState
    {
        public int Generation { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }
    }

    private int _previewGeneration;

    public FolderPanelView()
    {
        InitializeComponent();
    }

    private FolderPanelViewModel? ViewModel => DataContext as FolderPanelViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || ViewModel is not { } panel || !list.IsVisible)
        {
            return;
        }

        panel.SelectedItems = [.. list.SelectedItems.OfType<FileItemViewModel>()];
    }

    private void OnShellIconImageDataContextChanged(
        object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileItemViewModel oldItem)
        {
            oldItem.CancelPendingShellIconFetch();
        }
    }

    private void OnShellIconImageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image { DataContext: FileItemViewModel item })
        {
            item.CancelPendingShellIconFetch();
        }
    }

    private void OnLargeIconImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            LoadLargeIconAsync(image);
        }
    }

    private void OnLargeIconImageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var state = _largeIconRequests.GetOrCreateValue(image);
        state.Generation++;
        state.Cancellation?.Cancel();
        image.Source = null;
    }

    private void OnLargeIconImageDataContextChanged(
        object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Image { IsLoaded: true } image)
        {
            LoadLargeIconAsync(image);
        }
    }

    private async void LoadLargeIconAsync(Image image)
    {
        var state = _largeIconRequests.GetOrCreateValue(image);
        var generation = ++state.Generation;
        state.Cancellation?.Cancel();
        image.Source = null;

        if (image.DataContext is not FileItemViewModel item)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;

        var dpi = VisualTreeHelper.GetDpi(image);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(image.Width * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(image.Height * dpi.DpiScaleY));

        BitmapSource? visual;
        try
        {
            visual = await _previewThumbnailService.GetShellVisualAsync(
                item.FullPath, item.IsDirectory, pixelWidth, pixelHeight, cancellation.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch
        {

            return;
        }
        finally
        {
            if (ReferenceEquals(state.Cancellation, cancellation))
            {
                state.Cancellation = null;
            }

            cancellation.Dispose();
        }

        if (!image.IsLoaded || state.Generation != generation
            || !ReferenceEquals(image.DataContext, item))
        {
            return;
        }

        image.Source = visual;
    }

    private void OnLargeIconListIsVisibleChanged(
        object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

        if (!list.IsVisible)
        {
            foreach (var image in FindVisualChildren<Image>(list))
            {
                var state = _largeIconRequests.GetOrCreateValue(image);
                state.Generation++;
                state.Cancellation?.Cancel();
                image.Source = null;
            }

            return;
        }

        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            list.InvalidateMeasure();
            FindVisualChild<VirtualizingWrapPanel>(list)?.InvalidateMeasure();

            foreach (var image in FindVisualChildren<Image>(list))
            {
                LoadLargeIconAsync(image);
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel || panel.IsRecursiveSearch)
        {
            return;
        }

        if (e.OriginalSource is not GridViewColumnHeader { Role: GridViewColumnHeaderRole.Normal } header)
        {
            return;
        }

        if (header.Column is not { } column || ColumnSort.GetSortKey(column) is not { } sort)
        {
            return;
        }

        panel.SetSortCommand.Execute(sort);
    }

    private void OnSelectionCleared(object? sender, EventArgs e)
    {
        DetailsList.UnselectAll();
        SimpleList.UnselectAll();
        LargeIconList.UnselectAll();
        ExtraLargeIconList.UnselectAll();
    }

    private void OnSelectionRestored(object? sender, EventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        Reapply(DetailsList, panel);
        Reapply(SimpleList, panel);
        Reapply(LargeIconList, panel);
        Reapply(ExtraLargeIconList, panel);

        static void Reapply(ListView list, FolderPanelViewModel panel)
        {
            list.SelectedItems.Clear();
            foreach (var item in panel.SelectedItems)
            {
                list.SelectedItems.Add(item);
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FolderPanelViewModel old)
        {
            old.SelectionCleared -= OnSelectionCleared;
            old.SelectionRestored -= OnSelectionRestored;
            old.PropertyChanged -= OnPanelPropertyChanged;
        }

        if (e.NewValue is FolderPanelViewModel fresh)
        {
            fresh.SelectionCleared += OnSelectionCleared;
            fresh.SelectionRestored += OnSelectionRestored;
            fresh.PropertyChanged += OnPanelPropertyChanged;
        }

        ApplyPreviewLayout();
        ApplyPreviewContent();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderPanelViewModel.CurrentShowSize)
            or nameof(FolderPanelViewModel.CurrentShowModified)
            or nameof(FolderPanelViewModel.CurrentShowPosition)
            && ViewModel is { } panel
            && DetailsList.View is GridView grid)
        {
            ApplyColumnVisibility(grid, panel);
        }

        if (e.PropertyName is nameof(FolderPanelViewModel.CurrentShowPreview))
        {
            ApplyPreviewLayout();
            ApplyPreviewContent();
        }

        if (e.PropertyName is nameof(FolderPanelViewModel.PreviewTarget))
        {
            ApplyPreviewContent();
        }
    }

    private const double PreviewMinListHeight = 96;

    private const double PreviewMinPaneHeight = 120;

    private void ApplyPreviewLayout()
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        if (panel.CurrentShowPreview)
        {
            PreviewSplitter.Visibility = Visibility.Visible;
            PreviewPane.Visibility = Visibility.Visible;

            var ratio = panel.CurrentPreviewRatio;
            ListRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
            PreviewRow.Height = new GridLength(ratio, GridUnitType.Star);
            ListRow.MinHeight = PreviewMinListHeight;
            PreviewRow.MinHeight = PreviewMinPaneHeight;
        }
        else
        {
            PreviewSplitter.Visibility = Visibility.Collapsed;
            PreviewPane.Visibility = Visibility.Collapsed;

            PreviewHost.UnloadPreview();

            PreviewRow.Height = new GridLength(0);
            PreviewRow.MinHeight = 0;
            ListRow.MinHeight = 0;
        }
    }

    private void ShowPreviewHost()
    {
        PreviewHost.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewFallbackText.Visibility = Visibility.Collapsed;
    }

    private void ShowPreviewImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        PreviewImage.Source = image;
        PreviewHost.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewFallbackText.Visibility = Visibility.Collapsed;
    }

    private void ShowPreviewText(string text)
    {
        PreviewFallbackText.Text = text;
        PreviewHost.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewFallbackText.Visibility = Visibility.Visible;
    }

    private async void ApplyPreviewContent()
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        var generation = ++_previewGeneration;

        if (panel.PreviewTarget is not { } target)
        {
            PreviewHost.UnloadPreview();
            ShowPreviewText("미리 볼 수 없습니다");
            return;
        }

        var width = PreviewPane.ActualWidth;
        var height = PreviewPane.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        PreviewHost.UnloadPreview();
        PreviewHost.Visibility = Visibility.Collapsed;

        var motwBlocked = MotwGuard.ShouldSkipPreviewHandler(target.FullPath);
        PreviewHandlerOutcome? handlerOutcome = null;

        if (!motwBlocked)
        {
            handlerOutcome = PreviewHost.LoadPreview(target.FullPath, (int)width, (int)height);
            if (handlerOutcome == PreviewHandlerOutcome.Shown)
            {
                ShowPreviewHost();
                return;
            }
        }

        BitmapSource? image;
        try
        {
            image = await _previewThumbnailService.GetThumbnailAsync(
                target.FullPath, (int)width, (int)height).ConfigureAwait(true);
        }
        catch (Exception)
        {

            image = null;
        }

        if (generation != _previewGeneration)
        {
            return;
        }

        if (image is not null)
        {
            ShowPreviewImage(image);
            return;
        }

        var text = motwBlocked
            ? "미리 볼 수 없습니다 (차단된 파일 — 속성에서 「차단 해제」하면 보입니다)"
            : handlerOutcome switch
            {
                PreviewHandlerOutcome.NoHandler => "미리 볼 수 없습니다 (미리보기 형식이 아닙니다)",
                PreviewHandlerOutcome.Failed => "미리 볼 수 없습니다 (미리보기를 여는 데 실패했습니다)",
                _ => "미리 볼 수 없습니다",
            };

        ShowPreviewText(text);
    }

    private void OnPreviewSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        var total = ListRow.ActualHeight + PreviewRow.ActualHeight;
        if (total <= 0)
        {
            return;
        }

        panel.SetPreviewRatio(PreviewRow.ActualHeight / total);
    }

    public void FocusSearch()
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        panel.ExpandSearch();

        Dispatcher.BeginInvoke(
            () =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void OnSearchToggleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        if (panel.IsSearchExpanded)
        {
            panel.CloseSearch();
        }
        else
        {
            FocusSearch();
        }
    }

    private void OnSearchFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            ViewModel?.CollapseSearchIfEmpty();
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: FileItemViewModel item } && ViewModel is { } panel)
        {
            panel.OpenCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _pendingSingleSelect = null;
        _pendingSingleSelectList = null;

        _dragPressedItem = sender is ListViewItem { DataContext: FileItemViewModel pressed } ? pressed : null;

        if (Keyboard.Modifiers != ModifierKeys.None
            || sender is not ListViewItem { IsSelected: true, DataContext: FileItemViewModel item } container
            || ViewModel is not { SelectedItems.Count: > 1 })
        {
            return;
        }

        _pendingSingleSelect = item;
        _pendingSingleSelectList = ItemsControl.ItemsControlFromItemContainer(container) as ListView;
        e.Handled = true;
    }

    private DropWatch ObserveDropsInThisWindow() => new(Window.GetWindow(this));

    private sealed class DropWatch : IDisposable
    {
        private readonly Window? _window;
        private readonly DragEventHandler _handler;

        public DropWatch(Window? window)
        {
            _window = window;
            _handler = (_, _) => Fired = true;
            _window?.AddHandler(DragDrop.PreviewDropEvent, _handler, handledEventsToo: true);
        }

        public bool Fired { get; private set; }

        public void Dispose() => _window?.RemoveHandler(DragDrop.PreviewDropEvent, _handler);
    }

    private Point? _bandOrigin;

    private ListView? _bandList;

    private bool _bandActive;

    private readonly List<object> _bandKept = [];

    private KeyEventHandler? _bandEscape;

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var kind = RubberBand.Classify(RubberBand.AncestorTypes(e.OriginalSource, this));

        if (kind != ListHitKind.Empty || sender is not ListView list)
        {
            return;
        }

        _bandOrigin = e.GetPosition(ListArea);
        _bandList = list;
    }

    private void OnListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var ancestors = RubberBand.AncestorTypes(e.OriginalSource, this);

        if (ancestors.Any(t => typeof(GridViewColumnHeader).IsAssignableFrom(t)))
        {

            return;
        }

        if (ViewModel is { IsSearchTile: true })
        {
            e.Handled = true;
            return;
        }

        if (RubberBand.Classify(ancestors) != ListHitKind.Empty)
        {
            e.Handled = true;
        }
    }

    private void OnBreadcrumbScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
        {
            BreadcrumbScroll.ScrollToRightEnd();
        }
    }

    private void OnPanelPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is { } dragOrigin
            && e.LeftButton == MouseButtonState.Pressed
            && ViewModel is { } panel
            && ItemDragThreshold.PastThreshold(dragOrigin, e.GetPosition(this)))
        {

            var pressedItem = _dragPressedItem;
            _dragOrigin = null;
            _pendingSingleSelect = null;
            _pendingSingleSelectList = null;
            _dragPressedItem = null;

            if (panel.BuildDragPayload(pressedItem) is not { } payload)
            {
                return;
            }

            var data = new DataObject();
            data.SetData(FileDragFormat, DragPayloadCodec.Pack(payload));

            ShellExport.Attach(data, payload.Items);

            var landedInside = ObserveDropsInThisWindow();

            DragDropEffects effect;

            using (FpuGuard.Enter("DoDragDrop"))
            {
                effect = DragDrop.DoDragDrop(this, data, ShellExport.AllowedEffects);
            }

            landedInside.Dispose();

            if (!landedInside.Fired && effect != DragDropEffects.None)
            {
                panel.ReportExported?.Invoke(payload.Items.Count);
            }

            return;
        }

        if (_bandOrigin is not { } origin)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {

            EndBand();
            return;
        }

        var now = e.GetPosition(ListArea);

        if (!_bandActive)
        {
            if (!RubberBand.PastThreshold(origin, now))
            {

                return;
            }

            BeginBand();
        }

        var band = RubberBand.Between(origin, now);

        Canvas.SetLeft(Band, band.X);
        Canvas.SetTop(Band, band.Y);
        Band.Width = band.Width;
        Band.Height = band.Height;

        ApplySelection(band);
    }

    private void OnPanelPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = null;
        _dragPressedItem = null;

        if (_pendingSingleSelect is { } item && _pendingSingleSelectList is { } list)
        {
            list.SelectedItems.Clear();
            list.SelectedItem = item;
        }

        _pendingSingleSelect = null;
        _pendingSingleSelectList = null;

        EndBand();
    }

    private void OnPanelLostMouseCapture(object sender, MouseEventArgs e) => EndBand();

    private void BeginBand()
    {
        _bandActive = true;

        _bandList!.Focus();

        var adds = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        _bandKept.Clear();
        _bandKept.AddRange(RubberBand.Kept(adds, [.. _bandList.SelectedItems.Cast<object>()]));

        if (!adds)
        {
            _bandList.SelectedItems.Clear();
        }

        Band.Visibility = Visibility.Visible;
        CaptureMouse();

        _bandEscape = (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                EndBand();
                args.Handled = true;
            }
        };

        Window.GetWindow(this)?.AddHandler(Keyboard.PreviewKeyDownEvent, _bandEscape, handledEventsToo: true);
    }

    private void EndBand()
    {
        var escape = _bandEscape;
        var wasActive = _bandActive;

        _bandOrigin = null;
        _bandList = null;
        _bandActive = false;
        _bandEscape = null;
        _bandKept.Clear();

        if (escape is not null)
        {
            Window.GetWindow(this)?.RemoveHandler(Keyboard.PreviewKeyDownEvent, escape);
        }

        if (!wasActive)
        {
            return;
        }

        Band.Visibility = Visibility.Collapsed;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void ApplySelection(Rect band)
    {
        if (_bandList is not { } list)
        {
            return;
        }

        var wanted = RubberBand.Wanted(_bandKept, RealizedRows(list), band);

        foreach (var stale in list.SelectedItems.Cast<object>().Where(o => !wanted.Contains(o)).ToList())
        {
            list.SelectedItems.Remove(stale);
        }

        foreach (var fresh in wanted.Where(o => !list.SelectedItems.Contains(o)))
        {
            list.SelectedItems.Add(fresh);
        }
    }

    private IEnumerable<(object Item, Rect Bounds)> RealizedRows(ListView list)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not ListViewItem { IsVisible: true } row)
            {
                continue;
            }

            yield return (
                list.Items[i],
                row.TransformToAncestor(ListArea).TransformBounds(new Rect(default, row.RenderSize)));
        }
    }

    private GridViewColumn? _flexColumn;

    private double _flexWidth = double.NaN;

    private GridView? _columnVisibilityGrid;

    private List<GridViewColumn>? _originalColumns;

    private void ApplyColumnVisibility(GridView grid, FolderPanelViewModel panel)
    {
        if (!ReferenceEquals(grid, _columnVisibilityGrid))
        {
            _columnVisibilityGrid = grid;
            _originalColumns = [.. grid.Columns];
        }

        var desired = _originalColumns!
            .Where(c => ColumnVisibility.ShouldShow(c, panel.CurrentShowSize, panel.CurrentShowModified, panel.CurrentShowPosition))
            .ToList();

        if (grid.Columns.Count == desired.Count && grid.Columns.SequenceEqual(desired))
        {
            return;
        }

        grid.Columns.Clear();
        foreach (var column in desired)
        {
            grid.Columns.Add(column);
        }

        _flexColumn = null;
        _flexWidth = double.NaN;
    }

    private void OnDetailsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ListView { View: GridView grid } || ViewModel is not { } panel)
        {
            return;
        }

        ApplyColumnVisibility(grid, panel);

        if (ColumnLayout.FlexColumn(grid) is not { } column)
        {
            return;
        }

        var swapped = !ReferenceEquals(column, _flexColumn);

        if (!swapped && e.ViewportWidthChange == 0)
        {
            return;
        }

        if (e.ViewportWidth <= 0)
        {
            return;
        }

        var others = grid.Columns.Where(c => !ReferenceEquals(c, column)).Sum(c => c.Width);
        var wanted = ColumnLayout.FlexWidth(
            e.ViewportWidth, others, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth);

        _flexColumn = column;

        if (Math.Abs(wanted - _flexWidth) < 0.5 && Math.Abs(wanted - column.Width) < 0.5)
        {
            return;
        }

        _flexWidth = wanted;
        column.Width = wanted;
    }

    private void OnItemPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem { IsSelected: false } container
            || ItemsControl.ItemsControlFromItemContainer(container) is not ListView list)
        {
            return;
        }

        list.SelectedItems.Clear();
        container.IsSelected = true;
    }

    private void OnItemPreviewRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem { DataContext: FileItemViewModel item }
            || ViewModel is not { } panel
            || PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var chosen = panel.SelectedItems.Count > 0 && panel.SelectedItems.Contains(item)
            ? panel.SelectedItems
            : [item];

        var paths = chosen.Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        e.Handled = true;

        if (!ShellMenuPolicy.SharesOneParent(paths))
        {
            panel.ReportShellMenuRejection("셸 메뉴는 한 폴더 안에서 고른 것에만 — 지금 여러 폴더가 섞였다");
            return;
        }

        var screen = PointToScreen(e.GetPosition(this));

        var canCompareContent = chosen.Count == 1
            && !item.IsDirectory
            && item.CompareStatus == CompareStatus.Different;

        try
        {
            var outcome = ShellItemMenu.Show(
                source, paths, item.IsDirectory, canCompareContent, (int)screen.X, (int)screen.Y);

            if (outcome.ShellHr != 0)
            {
                panel.ReportShellMenuFailure($"메뉴 항목을 실행하지 못했다 — 0x{outcome.ShellHr:X8}");
            }

            RunAppCommand(panel, item, outcome.App);
        }
        catch (Exception ex)
        {

            panel.ReportShellMenuFailure($"우클릭 메뉴를 띄우지 못했다 — {ex.Message}");
        }
    }

    private static void RunAppCommand(
        FolderPanelViewModel panel, FileItemViewModel item, ShellMenuAppCommand app)
    {
        switch (app)
        {
            case ShellMenuAppCommand.RevealInExplorer:
                panel.RevealItemCommand.Execute(item);
                break;

            case ShellMenuAppCommand.Rename:
                panel.RenameItemCommand.Execute(item);
                break;

            case ShellMenuAppCommand.SetAnchor:
                panel.SetAnchorToItemCommand.Execute(item);
                break;

            case ShellMenuAppCommand.RegisterFolder:
                panel.RegisterFolderCommand?.Execute(item);
                break;

            case ShellMenuAppCommand.Trash:
                panel.TrashSelectionCommand?.Execute(null);
                break;

            case ShellMenuAppCommand.CompareContent:
                panel.CompareContentCommand.Execute(item);
                break;
        }
    }

    private void OnTileDragOver(object sender, DragEventArgs e)
    {

        e.Effects = ResolveDrop(e) is { } drop
            ? DropRouter.Effect(drop.Copy)
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnTileDragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is { } panel)
        {
            panel.IsDropTarget = false;
        }
    }

    private async void OnTileDrop(object sender, DragEventArgs e)
    {
        var drop = ResolveDrop(e);

        if (ViewModel is { } panel)
        {
            panel.IsDropTarget = false;
        }

        DropRouter.TraceDrop(
            "tile", DropRouter.Route(e.Data), drop?.Destination, drop?.Items.Count ?? 0);

        if (drop is not { } accepted)
        {
            return;
        }

        e.Handled = true;

        await accepted.Host.DropOntoPathAsync(
            accepted.Destination, accepted.Items, accepted.Copy);
    }

    private (IShellDropHost Host, string Destination, IReadOnlyList<OperationItem> Items, bool Copy)?
        ResolveDrop(DragEventArgs e)
    {
        if (ViewModel is not { ShellDropDestination: { } destination, ShellDropHost: { } host } panel
            || DropRouter.Resolve(e.Data, e.KeyStates, destination) is not { } incoming
            || !host.CanDropOnto(destination, incoming.Items))
        {
            return null;
        }

        panel.IsDropTarget = true;

        panel.DropTargetCopies = incoming.Copy;
        return (host, destination, incoming.Items, incoming.Copy);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        switch (e.ChangedButton)
        {
            case MouseButton.Left:

                if (!IsKeyboardFocusWithin
                    && PanelFocusPolicy.ShouldFocusPanelOnLeftClick(e.OriginalSource as DependencyObject))
                {
                    Focus();
                }

                break;

            case MouseButton.XButton1:

                if (panel.GoBackCommand.CanExecute(null))
                {
                    panel.GoBackCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case MouseButton.XButton2:
                if (panel.GoForwardCommand.CanExecute(null))
                {
                    panel.GoForwardCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }
}
