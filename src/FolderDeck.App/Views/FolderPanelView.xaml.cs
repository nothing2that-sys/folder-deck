using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Operations;

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

        // 같은 드래그를 앱 밖에서도 읽을 수 있게 셸 포맷을 더 싣는다 (§4.9 · Stage 2).
        // 시작은 하나이고 갈림은 받는 쪽에서 일어난다 — 앱 안에서는 사설 포맷이 먼저 읽힌다.
        ShellExport.Attach(data, payload.Items);

        // 기본 이동 · Ctrl 복사는 놓는 쪽이 정한다 — 여기서는 둘 다 허용만 한다.
        var landedInside = ObserveDropsInThisWindow();
        var effect = DragDrop.DoDragDrop(this, data, ShellExport.AllowedEffects);
        landedInside.Dispose();

        // 밖으로 나갔을 때만 알린다. 앱 안에 놓였으면 그쪽 경로가 자기 결과를 적는다(§4.2-2) —
        // 여기서 한 줄 더 쓰면 그 결과를 덮는다.
        if (!landedInside.Fired && effect != DragDropEffects.None)
        {
            panel.ReportExported?.Invoke(payload.Items.Count);
        }
    }

    /// <summary>
    /// 이 드래그가 <b>이 창 안</b>에 놓였는지만 본다 (§4.9 — 결과 보고의 갈림).
    /// </summary>
    /// <remarks>
    /// <c>DoDragDrop</c> 의 반환값은 받아졌는가만 말하고 <b>어디에 놓였는지는 말하지 않는다.</b>
    /// 앱 안의 폴더 행·대상함에 놓아도 <c>Copy</c>/<c>Move</c> 가 돌아오므로, 반환값만 보고
    /// "밖으로 넘겼다"를 적으면 내부 드롭에도 그 문구가 뜬다. 내부 드롭의 결과 보고는
    /// <c>MainWindow.OnFileDrop</c> 이 <c>async void</c> 라 조금 뒤에 오고, 그 사이 이 문구가
    /// 상태 바를 차지한다.
    /// <para>
    /// <c>PreviewDrop</c> 은 창(루트)에서 과녁으로 <b>내려가는</b> 이벤트라 어느 자리에 놓이든
    /// 창이 먼저 본다. <b>받는 쪽 코드는 건드리지 않는다</b> — 여기서 붙였다 떼는 것이 전부다.
    /// </para>
    /// </remarks>
    private DropWatch ObserveDropsInThisWindow() => new(Window.GetWindow(this));

    /// <summary>드래그 한 번 동안만 창에 붙어 있는 관찰자.</summary>
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

        /// <summary>이 창 안에서 드롭이 일어났는가.</summary>
        public bool Fired { get; private set; }

        public void Dispose() => _window?.RemoveHandler(DragDrop.PreviewDropEvent, _handler);
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

    // ── 셸에서 끌어온 것 받기 (§4.9 · Phase 5 Stage 1) ────────────────────────
    //
    // 타일이 셸 드롭의 과녁이 된다. 내부 드래그(§4.2-2)는 타일에서 <b>시작</b>만 하고 타일로
    // <b>놓지는</b> 않으므로, 이것은 기존 자리에 분기를 더하는 것이 아니라 새 과녁이다.

    /// <summary>
    /// 과녁 위에 있는 동안 계속 불린다 — 강조를 켜고 커서 모양(복사/이동)을 정한다.
    /// <b><c>DragEnter</c> 도 이 핸들러를 쓴다.</b>
    /// </summary>
    /// <remarks>
    /// WPF 는 드래그 이벤트를 히트테스트로 찾은 <b>자손</b>에 올리고 그것이 <c>AllowDrop</c> 을
    /// 가진 이 컨트롤까지 버블한다. 그래서 커서가 이 컨트롤 안에서 자식 하나를 벗어나 옆
    /// 자식으로 옮겨가기만 해도 — 포인터는 이 컨트롤 밖으로 나간 적이 없는데 —
    /// <c>DragLeave</c> 가 올라와 강조가 한 번 꺼진다. 그 뒤 <c>DragEnter</c> 가 곧바로 따라오므로
    /// 두 이벤트에 같은 판정을 물려 두면 꺼진 자리가 같은 호출 안에서 다시 켜져 눈에 보이지 않는다.
    /// </remarks>
    private void OnShellDragOver(object sender, DragEventArgs e)
    {
        // 받지 않으면 과녁을 켜지 않고 커서도 "안 됨"이다 (§4.2-2).
        e.Effects = ResolveShellDrop(e) is null
            ? DragDropEffects.None
            : DropRouter.ShellEffect(e.KeyStates);

        e.Handled = true;
    }

    private void OnShellDragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is { } panel)
        {
            panel.IsDropTarget = false;
        }
    }

    private async void OnShellDrop(object sender, DragEventArgs e)
    {
        var drop = ResolveShellDrop(e);

        if (ViewModel is { } panel)
        {
            panel.IsDropTarget = false;
        }

        // 어느 분기로 갔는지 남긴다 — 받지 않은 드롭도 이유가 로그에 보여야 한다.
        DropRouter.TraceDrop(
            "tile", DropRouter.Route(e.Data), drop?.Destination, drop?.Items.Count ?? 0);

        if (drop is not { } accepted)
        {
            return;
        }

        e.Handled = true;

        // 수정키는 <b>놓는 순간</b>으로 판정한다 — 기본 복사, Shift 면 이동 (§4.9).
        // 조작 실패는 결과로 보고되므로 여기까지 예외로 올라오지 않는다 (§5).
        await accepted.Host.DropOntoPathAsync(
            accepted.Destination, accepted.Items, DropRouter.ShellDropCopies(e.KeyStates));
    }

    /// <summary>
    /// 받을 수 있는 드롭이면 (통로, 목적지, 항목). 아니면 null. 과녁 강조도 여기서 켠다 —
    /// 판정과 강조가 갈라지면 "파랗게 켜졌는데 안 받는" 자리가 생긴다.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> 와 <c>Drop</c> 이 <b>이 함수 하나</b>를 쓴다. 사설 포맷이 실린 드롭은
    /// <see cref="DropRouter"/> 가 <see cref="DropRoute.Internal"/> 로 갈라 주고, 타일은 내부
    /// 드래그를 받는 자리가 아니므로(받는 자리는 왼쪽 폴더 행과 대상함뿐이다 — §4.2-2) 여기서
    /// 조용히 끝난다.
    /// </remarks>
    private (IShellDropHost Host, string Destination, IReadOnlyList<OperationItem> Items)?
        ResolveShellDrop(DragEventArgs e)
    {
        if (DropRouter.Route(e.Data) != DropRoute.Shell
            || ViewModel is not { ShellDropDestination: { } destination, ShellDropHost: { } host } panel)
        {
            return null;
        }

        var items = DropRouter.ShellItems(e.Data);

        if (!host.CanDropOnto(destination, items))
        {
            return null;
        }

        panel.IsDropTarget = true;
        return (host, destination, items);
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
