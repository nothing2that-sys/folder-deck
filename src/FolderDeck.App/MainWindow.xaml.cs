using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderDeck.App.Interop;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Operations;

namespace FolderDeck.App;


public partial class MainWindow : Window
{

    private static readonly TimeSpan TransientMessageLife = TimeSpan.FromSeconds(4);

    private bool _firstActivationSeen;


    private bool _maximizeOnShow;


    private Point? _rowDragOrigin;





    private readonly DispatcherTimer _messageTimer;


    private GridLength _foldersHeight = new(2, GridUnitType.Star);
    private GridLength _lowerHeight = new(1, GridUnitType.Star);


    private GridLength _leftColumnWidth;
    private GridLength _leftSplitterWidth;
    private double _leftColumnMinWidth;


    private readonly IFolderPicker _picker;

    public MainWindow(MainViewModel viewModel, IFolderPicker picker)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        _picker = picker;

        _messageTimer = new DispatcherTimer { Interval = TransientMessageLife };
        _messageTimer.Tick += OnMessageExpired;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;


        _leftColumnWidth = LeftColumn.Width;
        _leftSplitterWidth = LeftSplitterColumn.Width;
        _leftColumnMinWidth = LeftColumn.MinWidth;



        ApplyLeftRowHeights();
        ApplyLeftPanelWidth();

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    public MainViewModel ViewModel { get; }













    public void ApplyPlacement(WindowPlacementResult placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Bounds.X;
        Top = placement.Bounds.Y;
        Width = placement.Bounds.Width;
        Height = placement.Bounds.Height;

        _maximizeOnShow = placement.Maximized;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_maximizeOnShow)
        {
            WindowState = WindowState.Maximized;
        }



        LocationChanged += OnPlacementChanged;
        SizeChanged += OnPlacementChanged;
        StateChanged += OnPlacementChanged;
    }

    private void OnPlacementChanged(object? sender, EventArgs e) => CapturePlacement();





    private void CapturePlacement()
    {
        if (WindowState == WindowState.Minimized)
        {

            return;
        }

        var bounds = WindowState == WindowState.Maximized
            ? new ScreenRect(RestoreBounds.X, RestoreBounds.Y, RestoreBounds.Width, RestoreBounds.Height)
            : new ScreenRect(Left, Top, ActualWidth, ActualHeight);

        ViewModel.UpdateWindowPlacement(bounds, WindowState == WindowState.Maximized);
    }

    protected override async void OnActivated(EventArgs e)
    {
        base.OnActivated(e);


        if (!_firstActivationSeen)
        {
            _firstActivationSeen = true;
            return;
        }






        if (ViewModel.IsOperationRunning || ViewModel.IsShowingComparePicker)
        {
            return;
        }


        await ViewModel.RefreshAllCommand.ExecuteAsync(null);
    }





    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.F || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        var panels = FindPanels(this).ToList();
        var target = panels.FirstOrDefault(p => p.IsKeyboardFocusWithin)
                     ?? panels.FirstOrDefault(p => ReferenceEquals(p.DataContext, ViewModel.RotatingPanel));

        if (target is not null)
        {
            target.FocusSearch();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {

            case nameof(MainViewModel.Message):
                _messageTimer.Stop();
                if (ViewModel.HasMessage && ViewModel.MessageIsTransient)
                {
                    _messageTimer.Start();
                }

                break;

            case nameof(MainViewModel.FoldersCollapsed):
            case nameof(MainViewModel.LowerCollapsed):
                ApplyLeftRowHeights();
                break;

            case nameof(MainViewModel.LeftPanelCollapsed):
                ApplyLeftPanelWidth();
                break;
        }
    }














    private void ApplyLeftPanelWidth()
    {
        if (ViewModel.LeftPanelCollapsed)
        {
            if (LeftColumn.Width.IsStar)
            {
                _leftColumnWidth = LeftColumn.Width;
            }

            LeftColumn.MinWidth = 0;
            LeftColumn.Width = new GridLength(0);
            LeftSplitterColumn.Width = new GridLength(0);
        }
        else
        {
            LeftColumn.MinWidth = _leftColumnMinWidth;
            LeftColumn.Width = _leftColumnWidth;
            LeftSplitterColumn.Width = _leftSplitterWidth;
        }
    }











    private void ApplyLeftRowHeights()
    {
        Apply(FoldersRow, ViewModel.FoldersCollapsed, ref _foldersHeight);
        Apply(LowerRow, ViewModel.LowerCollapsed, ref _lowerHeight);

        static void Apply(RowDefinition row, bool collapsed, ref GridLength remembered)
        {
            if (collapsed)
            {
                if (row.Height.IsStar)
                {
                    remembered = row.Height;
                }

                row.Height = GridLength.Auto;
            }
            else if (row.Height.IsAuto)
            {


                row.Height = remembered;
            }
        }
    }





    private void OnTrayCheckChanged(object sender, RoutedEventArgs e) =>
        ViewModel.NotifyOperationState();

    private void OnAddFoldersClick(object sender, RoutedEventArgs e) =>
        ViewModel.AddFolders(_picker.PickFolders("작업 관리에 등록할 폴더 선택"));

    private void OnMessageExpired(object? sender, EventArgs e)
    {
        _messageTimer.Stop();
        ViewModel.ExpireTransientMessage();
    }



    private void OnFolderRowMouseDown(object sender, MouseButtonEventArgs e) =>
        _rowDragOrigin = ViewModel.IsEditingLayout ? e.GetPosition(this) : null;




    private void OnFolderRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_rowDragOrigin is not { } origin
            || e.LeftButton != MouseButtonState.Pressed
            || !ViewModel.IsEditingLayout
            || sender is not FrameworkElement { DataContext: FolderRowViewModel row })
        {
            return;
        }

        var moved = e.GetPosition(this) - origin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _rowDragOrigin = null;



        if (!row.CanPlaceInGrid)
        {
            ViewModel.RejectDuplicatePlacement(row);
            return;
        }

        var data = new DataObject();
        data.SetData(TileGridView.FolderRowFormat, row);
        using var fpu = FpuGuard.Enter("DoDragDrop(폴더 행)");
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }











    private void OnFileDropOver(object sender, DragEventArgs e)
    {
        e.Effects = ResolveDrop(sender, e) is { } drop
            ? DropRouter.Effect(drop.Copy)
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnFileDropLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IFileDropTarget target })
        {
            target.IsDropTarget = false;
        }
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        var drop = ResolveDrop(sender, e);

        if (sender is FrameworkElement { DataContext: IFileDropTarget cleared })
        {
            cleared.IsDropTarget = false;
        }


        DropRouter.TraceDrop(
            SiteOf(sender), DropRouter.Route(e.Data), drop?.Destination, drop?.Items.Count ?? 0);

        if (drop is not { } accepted)
        {
            return;
        }

        e.Handled = true;


        await ViewModel.DropOntoPathAsync(accepted.Destination, accepted.Items, accepted.Copy);
    }


    private readonly record struct ResolvedDrop(
        string Destination, IReadOnlyList<OperationItem> Items, bool Copy);
















    private ResolvedDrop? ResolveDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IFileDropTarget target }
            || DropRouter.Resolve(e.Data, e.KeyStates, target.Entry.Path) is not { } incoming
            || !ViewModel.CanDropOnto(target.Entry.Path, incoming.Items))
        {
            return null;
        }

        target.IsDropTarget = true;


        target.DropTargetCopies = incoming.Copy;
        return new ResolvedDrop(target.Entry.Path, incoming.Items, incoming.Copy);
    }


    private static string SiteOf(object sender) =>
        sender is FrameworkElement { DataContext: { } context } ? context.GetType().Name : "unknown";

    private static IEnumerable<FolderPanelView> FindPanels(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FolderPanelView panel)
            {
                yield return panel;
                continue;
            }

            foreach (var nested in FindPanels(child))
            {
                yield return nested;
            }
        }
    }






    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        CapturePlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        _messageTimer.Stop();
        _messageTimer.Tick -= OnMessageExpired;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        LocationChanged -= OnPlacementChanged;
        SizeChanged -= OnPlacementChanged;
        StateChanged -= OnPlacementChanged;


        ViewModel.Shutdown();
        base.OnClosed(e);
    }
}
