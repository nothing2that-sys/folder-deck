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

/// <summary>메인 창 (§3.2).</summary>
public partial class MainWindow : Window
{
    /// <summary>정보성 안내가 남아 있는 시간. 읽을 만큼은 되고 시야에 눌러앉지는 않는 길이.</summary>
    private static readonly TimeSpan TransientMessageLife = TimeSpan.FromSeconds(4);

    private bool _firstActivationSeen;

    /// <summary>저장된 마지막 상태가 최대화였는가 (§6.1). 창이 만들어진 뒤에 건다.</summary>
    private bool _maximizeOnShow;

    /// <summary>폴더 행에서 마우스를 누른 자리 — 드래그로 볼지 클릭으로 볼지 여기서 갈린다.</summary>
    private Point? _rowDragOrigin;

    /// <summary>
    /// 정보성 안내를 지우는 타이머. 시간 감각은 뷰의 몫이라 여기 둔다 —
    /// 뷰모델은 "사라져도 되는 메시지인가"만 안다(테스트에서 타이머를 기다리지 않아도 된다).
    /// </summary>
    private readonly DispatcherTimer _messageTimer;

    /// <summary>펴져 있을 때의 행 높이. 스플리터로 조정한 비율을 접었다 펴도 지키려고 기억한다.</summary>
    private GridLength _foldersHeight = new(2, GridUnitType.Star);
    private GridLength _lowerHeight = new(1, GridUnitType.Star);

    /// <summary>왼쪽 컬럼을 접기 전 모습 — 폭·하한·스플리터 칸. XAML 값을 그대로 받아 둔다.</summary>
    private GridLength _leftColumnWidth;
    private GridLength _leftSplitterWidth;
    private double _leftColumnMinWidth;

    /// <summary>폴더 고르기 대화상자. 창이 든다 — 뷰모델로 새지 않는다 (D38).</summary>
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

        // 접기가 되돌릴 "펴진 모습"은 XAML 이 정한 값이다. 숫자를 여기 또 적지 않는다.
        _leftColumnWidth = LeftColumn.Width;
        _leftSplitterWidth = LeftSplitterColumn.Width;
        _leftColumnMinWidth = LeftColumn.MinWidth;

        // 저장된 접힘 상태를 처음부터 반영한다. 통째 접기는 8-E ① 에서 저장 대상이 되면서
        // 여기 같이 붙었다 — 그 전에는 시작할 때 늘 펴져 있어 되돌릴 것이 없었다.
        ApplyLeftRowHeights();
        ApplyLeftPanelWidth();

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    public MainViewModel ViewModel { get; }

    // ── 창 위치 (§6.1 · Phase 4 Stage 2) ─────────────────────────────────────

    /// <summary>
    /// 띄우기 <b>전에</b> 자리를 잡아 준다. 화면 밖 방어까지 끝난 값이 들어온다
    /// (<see cref="FolderDeck.Core.Layout.WindowPlacement"/>).
    /// </summary>
    /// <remarks>
    /// 최대화는 여기서 걸지 않고 <see cref="OnSourceInitialized"/> 로 미룬다. 창을 만들기
    /// 전에 <c>WindowState</c> 를 최대화로 두면 WPF 가 <c>Left/Top</c> 을 아직 반영하지 않은
    /// 상태에서 <b>주 모니터</b>에 최대화해 버려, 다른 모니터에 저장해 둔 창이 주 모니터로
    /// 끌려온다.
    /// </remarks>
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

        // 자리를 다 잡은 뒤부터 저장한다 — 위에서 넣은 값이 그대로 되돌아 저장되는 것은
        // 무해하지만(같은 값이면 뷰모델이 흘린다), 창이 자리를 잡기 전 좌표는 쓰레기다.
        LocationChanged += OnPlacementChanged;
        SizeChanged += OnPlacementChanged;
        StateChanged += OnPlacementChanged;
    }

    private void OnPlacementChanged(object? sender, EventArgs e) => CapturePlacement();

    /// <summary>
    /// 지금 창 자리를 뷰모델에 넘긴다. 최대화·최소화 중이면 <c>RestoreBounds</c> 를 쓴다 —
    /// 최대화된 사각형을 저장하면 최대화를 풀었을 때 돌아갈 자리가 없어진다.
    /// </summary>
    private void CapturePlacement()
    {
        if (WindowState == WindowState.Minimized)
        {
            // 최소화 중에는 최대화 여부도 좌표도 뜻이 없다. 마지막으로 저장한 값을 그대로 둔다.
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

        // 첫 활성화는 Loaded 의 초기 로드와 겹치므로 건너뛴다.
        if (!_firstActivationSeen)
        {
            _firstActivationSeen = true;
            return;
        }

        // 파일 조작 중이면 건너뛴다 — 확인·결과 대화상자가 이 활성화를 일으키는데,
        // 조작이 끝나고 스스로 한 번 읽으므로 여기서 또 읽으면 열거가 겹친다.
        // 타일 비교 상대를 고르는 PickOneDialog 도 같은 이유로 건너뛴다(2026-09-21 실측
        // 손 확인 — 대화상자가 닫히며 이 활성화가 걸리고, 그 재조회가 RunCompare 가 막
        // 걸어 둔 비교를 LoadCompleted 로 곧바로 지워서 배지가 하나도 안 보였다).
        if (ViewModel.IsOperationRunning || ViewModel.IsShowingComparePicker)
        {
            return;
        }

        // 창으로 돌아오면 재조회 (§8 — 수동 F5 + 창 활성화 시 재조회).
        await ViewModel.RefreshAllCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// <c>Ctrl+F</c> → 활성 패널의 검색창을 펴고 포커스. "활성"은 키보드 포커스가 든 패널이고,
    /// 아무 패널에도 없으면 순환 칸으로 보낸다(리스트나 빈 곳에 포커스가 있는 경우).
    /// </summary>
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
            // 새 메시지가 뜰 때마다 수명을 다시 잰다. 남아야 할 메시지면 타이머를 세운다.
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

    /// <summary>
    /// 왼쪽 컬럼 통째 접기 (Phase 8-A ⑦) — 컬럼 폭과 스플리터 칸을 0 으로 만든다.
    /// </summary>
    /// <remarks>
    /// <b><see cref="ColumnDefinition.MinWidth"/> 도 같이 내려야 한다.</b> 폭만 0 으로 두면
    /// 하한(220)이 이겨 접어도 빈 칸이 그대로 남는다. 스플리터 칸(6)도 같이 접지 않으면
    /// 패널이 사라진 자리에 잡을 것 없는 띠만 남는다.
    /// <para>
    /// 바인딩이 아니라 여기서 정하는 이유는 <see cref="ApplyLeftRowHeights"/> 와 같다 —
    /// <see cref="GridSplitter"/> 가 끌릴 때 로컬 값을 써서 바인딩을 지워 버린다. 그래서
    /// 스플리터로 맞춰 둔 폭은 접기 직전에 기억했다가 펼 때 되돌린다.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 왼쪽 컬럼 두 영역의 행 높이. 접히면 <c>Auto</c>(헤더만), 펴지면 별(star) 높이.
    /// </summary>
    /// <remarks>
    /// XAML 바인딩으로 두지 않는 이유: <see cref="GridSplitter"/> 는 끌 때
    /// <c>RowDefinition.Height</c> 에 <b>로컬 값</b>을 쓴다. 로컬 값은 바인딩을 지워 버리므로,
    /// 한 번이라도 스플리터를 끌면 그 뒤로는 접어도 행이 안 줄고 빈 자리가 남는다
    /// (실제로 그랬다). 그래서 높이는 여기서만 정하고, 스플리터가 잡아 둔 별 높이는
    /// 기억했다가 다시 펼 때 되돌려 준다 — 사용자가 맞춰 둔 비율을 접기 한 번으로 잃지 않게.
    /// </remarks>
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
                // 접혀 있던 행만 되돌린다. 이미 펴져 있는 행에 손대면 스플리터로 맞춰 둔
                // 높이를 다른 쪽을 접을 때마다 초기값으로 되돌려 버린다.
                row.Height = remembered;
            }
        }
    }

    /// <summary>
    /// 대상함 체크는 실행 대상 수를 바꾼다 — 요약과 버튼 활성 상태를 다시 계산시킨다.
    /// (개별 <c>IsChecked</c> 는 뷰모델마다라 루트가 스스로 알 방법이 없다.)
    /// </summary>
    private void OnTrayCheckChanged(object sender, RoutedEventArgs e) =>
        ViewModel.NotifyOperationState();

    private void OnAddFoldersClick(object sender, RoutedEventArgs e) =>
        ViewModel.AddFolders(_picker.PickFolders("작업 관리에 등록할 폴더 선택"));

    private void OnMessageExpired(object? sender, EventArgs e)
    {
        _messageTimer.Stop();
        ViewModel.ExpireTransientMessage();
    }

    // ── 왼쪽 폴더 리스트 → 빈 칸 드래그 (배치 편집 모드, Phase 2) ────────────

    private void OnFolderRowMouseDown(object sender, MouseButtonEventArgs e) =>
        _rowDragOrigin = ViewModel.IsEditingLayout ? e.GetPosition(this) : null;

    /// <summary>
    /// 임계값을 넘겨 끌면 드래그로 본다. 그 아래면 그대로 두어 버튼 클릭(=순환 칸 교체)이 산다.
    /// </summary>
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

        // 이미 상시 칸에 있는 폴더는 끌어봐야 놓을 데가 없다. 드래그를 시작하지 않고
        // 그 자리에서 이유를 말한다 — 끌고 다니다 거절당하는 것보다 낫다.
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

    // ── 폴더 행 / 대상함 드롭 (§4.2-2 Phase 3 Stage 3 · §4.9 Phase 5 Stage 1) ──
    //
    // 두 자리는 이미 과녁이었고 사설 포맷을 받았다. Stage 1 이 더한 것은 <b>분기 하나</b>다 —
    // 셸에서 온 드롭도 같은 자리로 받는다("내부 드래그가 받는 자리와 같게 둔다", §4.9).

    /// <summary>
    /// 과녁 위에 있는 동안 계속 불린다 — 여기서 강조를 켜고 커서 모양(이동/복사)을 정한다.
    /// <b><c>DragEnter</c> 도 이 핸들러를 쓴다</b> — 이유는 <c>FolderPanelView.OnShellDragOver</c>
    /// 의 주석에 적었다(세 자리가 같은 이유로 그렇다).
    /// </summary>
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

        // 어느 분기로 갔는지 남긴다 — 받지 않은 드롭도 이유가 로그에 보여야 한다.
        DropRouter.TraceDrop(
            SiteOf(sender), DropRouter.Route(e.Data), drop?.Destination, drop?.Items.Count ?? 0);

        if (drop is not { } accepted)
        {
            return;
        }

        e.Handled = true;

        // 조작 실패는 결과로 보고되므로 여기까지 예외로 올라오지 않는다 (§5).
        await ViewModel.DropOntoPathAsync(accepted.Destination, accepted.Items, accepted.Copy);
    }

    /// <summary>받기로 판정된 드롭 하나 — 어느 규칙으로 왔는지까지 담는다.</summary>
    private readonly record struct ResolvedDrop(
        string Destination, IReadOnlyList<OperationItem> Items, bool Copy);

    /// <summary>
    /// 받을 수 있는 조합이면 (목적지, 항목, 복사여부). 아니면 null. 과녁 강조도 여기서 켠다 —
    /// 판정과 강조가 갈라지면 "파랗게 켜졌는데 안 받는" 자리가 생긴다.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> 와 <c>Drop</c> 이 <b>이 함수 하나</b>를 쓴다. 포맷 순서와 수정키 규칙은
    /// <see cref="DropRouter.Resolve"/> 에 있어 타일까지 세 자리가 같은 것을 본다 — 여기서
    /// 더하는 것은 <b>목적지</b>뿐이고, 그 목적지는 <c>Resolve</c> 에 넘긴다(볼륨 판정이 그것을
    /// 봐야 한다, §4.2-2).
    /// <para>
    /// 목적지는 <b>그 자리의 앵커 경로</b>다(<c>Entry.Path</c>). 타일의 <c>CurrentPath</c> 는
    /// 타일에만 있는 개념이다 — 대상함에 놓으면 <b>그 항목 하나</b>로만 가고 체크된 목적지
    /// 집합과 섞이지 않는다(§4.2-2).
    /// </para>
    /// </remarks>
    private ResolvedDrop? ResolveDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IFileDropTarget target }
            || DropRouter.Resolve(e.Data, e.KeyStates, target.Entry.Path) is not { } incoming
            || !ViewModel.CanDropOnto(target.Entry.Path, incoming.Items))
        {
            return null;
        }

        target.IsDropTarget = true;

        // 문구도 여기서 정한다 — 판정을 낸 자리와 갈라지면 "복사라고 써 놓고 옮기는" 자리가 생긴다.
        target.DropTargetCopies = incoming.Copy;
        return new ResolvedDrop(target.Entry.Path, incoming.Items, incoming.Copy);
    }

    /// <summary>진단 로그에서 어느 과녁이었는지 가려내려면 이름이 필요하다.</summary>
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

    /// <summary>
    /// 종료 시에도 창 자리를 확실히 한 번 쓴다 (§3.2) — 마지막 이동이 디바운스 안에서
    /// 죽지 않게. <see cref="OnClosed"/> 가 아니라 여기서 잡는 것은, 창이 이미 사라진 뒤엔
    /// <c>RestoreBounds</c> 가 뜻을 잃기 때문이다.
    /// </summary>
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

        // 밀린 디바운스 저장을 마무리한다 (§5).
        ViewModel.Shutdown();
        base.OnClosed(e);
    }
}
