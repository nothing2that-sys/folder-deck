using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;

namespace FolderDeck.App;

/// <summary>메인 창 (§3.2).</summary>
public partial class MainWindow : Window
{
    /// <summary>정보성 안내가 남아 있는 시간. 읽을 만큼은 되고 시야에 눌러앉지는 않는 길이.</summary>
    private static readonly TimeSpan TransientMessageLife = TimeSpan.FromSeconds(4);

    private bool _firstActivationSeen;

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

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;

        _messageTimer = new DispatcherTimer { Interval = TransientMessageLife };
        _messageTimer.Tick += OnMessageExpired;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 저장된 접힘 상태를 처음부터 반영한다.
        ApplyLeftRowHeights();

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    public MainViewModel ViewModel { get; }

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
        if (ViewModel.IsOperationRunning)
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
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    // ── 그리드 파일 → 폴더 행 / 대상함 드롭 (§4.2-2, Phase 3 Stage 3) ────────

    /// <summary>
    /// 과녁 위에 있는 동안 계속 불린다 — 여기서 강조를 켜고 커서 모양(이동/복사)을 정한다.
    /// </summary>
    private void OnFileDropOver(object sender, DragEventArgs e)
    {
        var target = ResolveDropTarget(sender, e);

        e.Effects = target is null
            ? DragDropEffects.None
            : IsCopyRequested(e) ? DragDropEffects.Copy : DragDropEffects.Move;

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
        var target = ResolveDropTarget(sender, e);

        if (sender is FrameworkElement { DataContext: IFileDropTarget cleared })
        {
            cleared.IsDropTarget = false;
        }

        if (target is not { } drop)
        {
            return;
        }

        e.Handled = true;

        // 수정키는 <b>놓는 순간</b>으로 판정한다 — 끌기 시작할 때 Ctrl 을 눌렀는지가 아니라.
        // 조작 실패는 결과로 보고되므로 여기까지 예외로 올라오지 않는다 (§5).
        await ViewModel.DropOntoFolderAsync(drop.Target.Entry, drop.Payload, IsCopyRequested(e));
    }

    /// <summary>탐색기 관용 — 기본은 이동, <c>Ctrl</c> 을 누르고 있으면 복사.</summary>
    private static bool IsCopyRequested(DragEventArgs e) =>
        (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

    /// <summary>
    /// 받을 수 있는 조합이면 (목적지 폴더, 뭉치). 아니면 null. 과녁 강조도 여기서 켠다 —
    /// 판정과 강조가 갈라지면 "파랗게 켜졌는데 안 받는" 자리가 생긴다.
    /// </summary>
    private (IFileDropTarget Target, FileDropPayload Payload)? ResolveDropTarget(
        object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IFileDropTarget target }
            || !e.Data.GetDataPresent(FolderPanelView.FileDragFormat)
            || e.Data.GetData(FolderPanelView.FileDragFormat) is not FileDropPayload payload
            || !ViewModel.CanDropOnto(target.Entry, payload))
        {
            return null;
        }

        target.IsDropTarget = true;
        return (target, payload);
    }

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

    protected override void OnClosed(EventArgs e)
    {
        _messageTimer.Stop();
        _messageTimer.Tick -= OnMessageExpired;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // 밀린 디바운스 저장을 마무리한다 (§5).
        ViewModel.Shutdown();
        base.OnClosed(e);
    }
}
