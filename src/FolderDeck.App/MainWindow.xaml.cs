using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;

namespace FolderDeck.App;

/// <summary>메인 창 (§3.2).</summary>
public partial class MainWindow : Window
{
    private bool _firstActivationSeen;

    /// <summary>폴더 행에서 마우스를 누른 자리 — 드래그로 볼지 클릭으로 볼지 여기서 갈린다.</summary>
    private Point? _rowDragOrigin;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
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

        var data = new DataObject();
        data.SetData(TileGridView.FolderRowFormat, row);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
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
        // 밀린 디바운스 저장을 마무리한다 (§5).
        ViewModel.Shutdown();
        base.OnClosed(e);
    }
}
