using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Views;

/// <summary>
/// 폴더 패널 — 상시 칸과 순환 칸이 같은 컨트롤을 쓴다 (§3.2).
/// 타일은 단순 표시기가 아니라 탐색기 뷰다(§4.6).
/// </summary>
public partial class FolderPanelView : UserControl
{
    /// <summary>그리드 → 폴더 행/대상함 드래그에 쓰는 앱 내부 포맷 (§4.2-2).</summary>
    public const string FileDragFormat = "FolderDeck.FileDrag";

    /// <summary>항목에서 마우스를 누른 자리 — 드래그로 볼지 클릭으로 볼지 여기서 갈린다.</summary>
    private Point? _dragOrigin;

    /// <summary>
    /// 누른 순간 이미 여럿 중 하나로 선택돼 있던 항목. 끌지 않고 놓았을 때만 단독 선택으로 접는다.
    /// </summary>
    private FileItemViewModel? _pendingSingleSelect;

    private ListView? _pendingSingleSelectList;

    public FolderPanelView()
    {
        InitializeComponent();
    }

    private FolderPanelViewModel? ViewModel => DataContext as FolderPanelViewModel;

    /// <summary>
    /// <c>ListView.SelectedItems</c> 는 바인딩할 수 없는 읽기 전용 컬렉션이라 여기서 옮긴다
    /// (§4.2 다중 선택). 두 ListView(details/list) 중 지금 보이는 쪽만 값을 낸다.
    /// </summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || ViewModel is not { } panel || !list.IsVisible)
        {
            return;
        }

        panel.SelectedItems = [.. list.SelectedItems.OfType<FileItemViewModel>()];
    }

    /// <summary>다른 타일이 선택을 가져갔다 — 목록에서도 실제로 풀어 준다.</summary>
    private void OnSelectionCleared(object? sender, EventArgs e)
    {
        DetailsList.UnselectAll();
        SimpleList.UnselectAll();
    }

    /// <summary>
    /// 목록을 다시 읽었다 — 뷰모델이 붙잡아 둔 항목을 목록에서도 다시 골라 준다 (§4.8).
    /// </summary>
    private void OnSelectionRestored(object? sender, EventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        Reapply(DetailsList, panel);
        Reapply(SimpleList, panel);

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
        }

        if (e.NewValue is FolderPanelViewModel fresh)
        {
            fresh.SelectionCleared += OnSelectionCleared;
            fresh.SelectionRestored += OnSelectionRestored;
        }
    }

    /// <summary>
    /// 검색창을 펴고 포커스를 준다 (<c>Ctrl+F</c> · 접힌 검색줄 클릭).
    /// </summary>
    public void FocusSearch()
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        panel.ExpandSearch();

        // 방금 보이게 된 TextBox 는 레이아웃이 한 번 돌아야 포커스를 받는다.
        Dispatcher.BeginInvoke(
            () =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    /// <summary>내비 클러스터의 🔍 — 토글이다. 열려 있으면 닫고, 닫혀 있으면 펴고 포커스한다.</summary>
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

    /// <summary>
    /// 검색 영역에서 포커스가 완전히 빠지면 접는다. 컨테이너 단위로 보기 때문에
    /// "하위 포함" 체크박스로 포커스가 옮겨가도 접히지 않는다.
    /// </summary>
    private void OnSearchFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            ViewModel?.CollapseSearchIfEmpty();
        }
    }

    /// <summary>
    /// 항목 더블클릭 (§4.5). 컨테이너에 붙여 두면 빈 공간 더블클릭으로는 발동하지 않는다.
    /// </summary>
    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: FileItemViewModel item } && ViewModel is { } panel)
        {
            panel.OpenCommand.Execute(item);
            e.Handled = true;
        }
    }

    // ── 항목 끌어내기 (§4.2-2 · Phase 3 Stage 3) ─────────────────────────────

    /// <summary>
    /// 누른 자리만 기억한다 — 여기서 드래그를 시작하면 클릭 한 번으로도 파일이 움직인다.
    /// </summary>
    /// <remarks>
    /// 여럿을 고른 뒤 그 위를 다시 누르면 WPF <see cref="ListView"/> 는 <b>그 자리에서</b>
    /// 나머지 선택을 풀어 버린다 — 뭉치를 통째로 끌 수 없게 된다. 그 경우만 이벤트를 막고,
    /// 끌지 않고 놓았을 때 단독 선택으로 접어 준다(탐색기와 같은 감각).
    /// </remarks>
    private void OnItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _pendingSingleSelect = null;
        _pendingSingleSelectList = null;

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

    private void OnItemPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = null;

        // 끌지 않고 놓았다 = 선택을 좁히려던 클릭이었다.
        if (_pendingSingleSelect is { } item && _pendingSingleSelectList is { } list)
        {
            list.SelectedItems.Clear();
            list.SelectedItem = item;
        }

        _pendingSingleSelect = null;
        _pendingSingleSelectList = null;
    }

    /// <summary>
    /// 임계값을 넘겨 끌면 드래그로 본다 (§4.2-2). 그 아래면 그대로 두어 클릭·더블클릭이 산다.
    /// </summary>
    private void OnItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is not { } origin
            || e.LeftButton != MouseButtonState.Pressed
            || ViewModel is not { } panel)
        {
            return;
        }

        var moved = e.GetPosition(this) - origin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragOrigin = null;
        _pendingSingleSelect = null;
        _pendingSingleSelectList = null;

        if (panel.BuildDragPayload() is not { } payload)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(FileDragFormat, payload);

        // 기본 이동 · Ctrl 복사는 놓는 쪽이 정한다 — 여기서는 둘 다 허용만 한다.
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    /// <summary>
    /// 우클릭한 항목이 선택 밖이면 그것만 고른다 (탐색기 동등). 메뉴의 "휴지통으로 삭제"가
    /// 선택 전체에 작용하므로, 무엇이 지워질지가 눌린 순간 목록에 보여야 한다.
    /// </summary>
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

    /// <summary>
    /// 마우스 뒤로/앞으로 버튼. 목록 안에서 눌러도 듣도록 터널링 단계에서 받는다 —
    /// 자식이 먼저 처리해 버리면 놓치기 때문이다.
    /// </summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        switch (e.ChangedButton)
        {
            case MouseButton.XButton1:
                // 스택이 비었으면 CanExecute 가 false 라 무동작이다.
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
