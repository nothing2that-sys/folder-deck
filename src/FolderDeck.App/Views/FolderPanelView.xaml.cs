using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FolderDeck.App.Interop;
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

    /// <summary>
    /// 헤더 메타의 정렬 글자를 눌렀다 — <b>메뉴를 여는 것이 전부다</b> (부채 17).
    /// </summary>
    /// <remarks>
    /// WPF <c>ContextMenu</c> 는 우클릭에만 저절로 열리므로 좌클릭으로 열려면 누군가
    /// <c>IsOpen</c> 을 세워야 하는데, 그것은 뷰가 가진 속성이라 VM 커맨드가 할 수 없다.
    /// <para>
    /// <b>정렬을 정하는 일은 여기에 없다.</b> 이 핸들러는 <c>SortBy</c> 를 읽지도 쓰지도
    /// 않는다 — 값을 바꾸는 것은 <c>MenuItem</c> 이 부르는 <c>SetSortCommand</c> 다.
    /// </para>
    /// </remarks>
    private void OnSortTextClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.ContextMenu is not { } menu)
        {
            return;
        }

        // PlacementTarget 을 먼저 세운다 — 메뉴가 그 글자 아래에 뜨고, 메뉴 안의 바인딩이
        // 이 버튼의 Tag(=패널 VM)를 타고 커맨드에 닿는다.
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// 상세 뷰의 컬럼 머리를 눌렀다 — <b>정렬 글자 메뉴와 같은 커맨드로 보낸다</b> (Phase 8-B 1).
    /// </summary>
    /// <remarks>
    /// <see cref="OnSortTextClick"/> 과 같은 결이다: 여기서 <c>SortBy</c> 를 읽지도 쓰지도 않고,
    /// 같은 기준을 다시 눌렀을 때 방향을 뒤집는 규칙도 여기 없다 — 전부
    /// <c>SetSortCommand</c>(<c>FolderPanelViewModel.SetSortAsync</c>) 안에 이미 있던 것이다.
    /// 이 핸들러가 하는 일은 <b>눌린 컬럼이 어느 기준인지</b> 골라 넘기는 것뿐이다.
    /// <para>
    /// 걸러야 하는 것이 셋이다.
    /// <list type="bullet">
    /// <item>폭 조절 손잡이와 오른쪽 남는 자리도 <c>GridViewColumnHeader</c> 다 —
    /// <c>Role</c> 이 <c>Normal</c> 인 것만 받는다(<c>Padding</c>·<c>Floating</c> 은 버린다).</item>
    /// <item>정렬 기준이 안 붙은 컬럼(검색 뷰의 「위치」)은 아무 일도 하지 않는다.</item>
    /// <item>재귀 검색 중에는 잠근다 — 그 결과는 찾은 순서대로 흘러들고 정렬하지 않으므로(§4.10)
    /// 기준을 바꿔도 순서가 안 바뀐다. 헤더 메타의 정렬 글자를 그동안 비활성으로 두는 것과
    /// 같은 판정을 같은 이유로 여기서도 한다.</item>
    /// </list>
    /// </para>
    /// </remarks>
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

    // ── 고무줄 선택 (§4.2-2 · Phase 5 Stage 3B) ───────────────────────────────
    //
    // 빈 곳에서 끌어 사각형으로 훑어 여러 개를 고른다. 판정과 기하는 <see cref="RubberBand"/> 에
    // 있고(뷰 밖에서 시험할 수 있게) 여기 남는 것은 <b>붙잡고 있는 상태</b>뿐이다.
    //
    // 이 앱의 세 번째 드래그다 — OLE(DoDragDrop) · 배치 편집의 캡처(TileGridView) 다음이다.
    // 섞이지 않는 근거: 아래 상태는 눌린 자리가 Empty 일 때만 세워지고, 항목 위 다운은
    // 기존 컨테이너 핸들러가 그대로 가져간다(e.Handled 를 세우지 않는다).

    /// <summary>고무줄 후보로 누른 자리 (<c>ListArea</c> 좌표). 임계를 넘기 전에는 이것만 있다.</summary>
    private Point? _bandOrigin;

    /// <summary>누른 자리가 든 목록. 항목 사각형을 이 목록에서만 뽑는다(뷰 방식이 둘이다).</summary>
    private ListView? _bandList;

    /// <summary>임계를 넘어 실제로 그리기 시작했나. <c>_bandOrigin</c> 만으로는 아직 클릭이다.</summary>
    private bool _bandActive;

    /// <summary>
    /// <c>Ctrl</c> 로 시작했을 때 지켜야 할 기존 선택. 사각형이 줄어들어도 이것은 남는다.
    /// </summary>
    private readonly List<object> _bandKept = [];

    /// <summary>
    /// <c>Esc</c> 를 받으려고 드래그 한 번 동안만 창에 붙는 핸들러 (§4-5).
    /// </summary>
    /// <remarks>
    /// 캡처는 <b>키보드를 옮겨 주지 않는다.</b> 빈 곳을 누르는 것으로 포커스가 이 패널에
    /// 들어오지 않을 수 있어, 패널의 <c>PreviewKeyDown</c> 에만 걸면 <c>Esc</c> 를 놓친다.
    /// 창에 붙였다 떼는 것은 Stage 2 의 <see cref="DropWatch"/> 와 같은 결이다.
    /// </remarks>
    private KeyEventHandler? _bandEscape;

    /// <summary>
    /// 목록에서 왼쪽 버튼을 눌렀다 — <b>여기서는 아무것도 바꾸지 않는다.</b> 누른 자리만 기억한다.
    /// </summary>
    /// <remarks>
    /// <b>두 <see cref="ListView"/> 가 이 핸들러 하나를 쓴다</b>(§6.1 의 두 뷰 방식). 뷰 방식마다
    /// 따로 쓰면 하나만 고쳐지는 사고가 난다.
    /// <para>
    /// 선택을 지우는 것도 캡처도 <b>임계를 넘은 뒤</b>다(<see cref="BeginBand"/>). 여기서 지우면
    /// 빈 곳을 그냥 클릭한 것만으로 선택이 날아간다 — §2 가 "빈 곳 클릭은 앱 코드가 아무것도
    /// 하지 않는다"고 확인했으므로 그 자리가 그대로 남아야 한다.
    /// </para>
    /// <para><c>e.Handled</c> 를 세우지 않는다 — 항목 위 다운은 컨테이너 핸들러 것이다.</para>
    /// </remarks>
    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var kind = RubberBand.Classify(RubberBand.AncestorTypes(e.OriginalSource, this));

        // §4-1 실측용 한 줄. 눌린 자리가 무엇으로 오는지는 짐작할 것이 아니다.
        if (kind != ListHitKind.Empty || sender is not ListView list)
        {
            return;
        }

        _bandOrigin = e.GetPosition(ListArea);
        _bandList = list;
    }

    /// <summary>
    /// 임계를 넘으면 고무줄을 시작하고, 시작한 뒤에는 사각형과 선택을 갱신한다.
    /// <b>항목 드래그 판정이 이 고무줄 흐름보다 먼저 온다</b> (D71).
    /// </summary>
    /// <remarks>
    /// 임계는 기존 드래그와 <b>같은 <see cref="SystemParameters"/></b> 를 쓴다 — 새 숫자를 만들지
    /// 않는다. 기준점도 같은 요소가 아니라 <c>ListArea</c> 지만, 두 값 다 같은 좌표계 안의
    /// 거리이므로 비교가 성립한다.
    /// <para>
    /// <b>왜 여기인가(D70·D71)</b> — 이 핸들러는 패널에 걸린 터널링 핸들러라 커서가 패널 안
    /// 어디에 있든 매번 불린다. 예전에는 항목(<c>ListViewItem</c>) 컨테이너에 걸려 있어 커서가
    /// 항목 밖으로 나가면 안 불렸다 — 그래서 빠르게 끌면 드래그가 시작되지 않았다.
    /// </para>
    /// </remarks>
    private void OnPanelPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is { } dragOrigin
            && e.LeftButton == MouseButtonState.Pressed
            && ViewModel is { } panel
            && ItemDragThreshold.PastThreshold(dragOrigin, e.GetPosition(this)))
        {
            // 예전 OnItemPreviewMouseMove 의 몸통 그대로다 — 자리만 옮겼다(D71).
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

            DragDropEffects effect;

            // OLE 드래그는 중첩 루프를 돌며 놓는 쪽의 코드를 우리 스레드에서 돌린다 —
            // 셸 메뉴와 같은 위험이다(F-1).
            using (FpuGuard.Enter("DoDragDrop"))
            {
                effect = DragDrop.DoDragDrop(this, data, ShellExport.AllowedEffects);
            }

            landedInside.Dispose();

            // 밖으로 나갔을 때만 알린다. 앱 안에 놓였으면 그쪽 경로가 자기 결과를 적는다(§4.2-2) —
            // 여기서 한 줄 더 쓰면 그 결과를 덮는다.
            if (!landedInside.Fired && effect != DragDropEffects.None)
            {
                panel.ReportExported?.Invoke(payload.Items.Count);
            }

            // 드래그를 시작했으면 여기서 끝낸다 — 고무줄 코드로 흘러가면 안 된다(D71).
            return;
        }

        if (_bandOrigin is not { } origin)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // 버튼을 놓친 채로 상태가 남는 경로를 막는다(창 밖에서 놓은 경우 등).
            EndBand();
            return;
        }

        var now = e.GetPosition(ListArea);

        if (!_bandActive)
        {
            if (!RubberBand.PastThreshold(origin, now))
            {
                // 아직 클릭이다 — 선택도 캡처도 건드리지 않는다.
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

    /// <summary>
    /// <c>EndBand()</c> 를 부르기 전에 예전 <c>OnItemPreviewMouseUp</c> 이 하던 일(선택 접기)을
    /// 먼저 한다 (D71) — 항목 컨테이너에 걸려 있던 그 핸들러는 커서가 항목 밖에서 놓이면
    /// 불리지 않아 선택이 미확정으로 남았다.
    /// </summary>
    private void OnPanelPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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

        EndBand();
    }

    /// <summary>캡처를 빼앗겼다 — 상태가 남으면 마우스가 계속 먹히지 않는 것처럼 보인다.</summary>
    private void OnPanelLostMouseCapture(object sender, MouseEventArgs e) => EndBand();

    /// <summary>
    /// 임계를 넘었다 — 여기서 선택을 정리하고 캡처와 <c>Esc</c> 를 건다.
    /// </summary>
    /// <remarks>
    /// <b>수정키는 이 순간으로 판정한다</b>(§3) — 내부 드롭이 "놓는 순간"으로 보는 것과 다르다.
    /// 기존 선택을 지울지가 시작할 때 정해지기 때문이다: 끌던 도중에 <c>Ctrl</c> 을 떼면 이미
    /// 지운 것을 되살릴 수가 없다.
    /// </remarks>
    private void BeginBand()
    {
        _bandActive = true;

        // 목록에 키보드 포커스를 준다 — <b>하이라이트가 이것에 달려 있다.</b>
        //
        // WPF 기본 ListViewItem 템플릿은 Selector.IsSelectionActive 가 거짓이면 "비활성 선택"
        // 브러시로 그린다. 실측: 흰 목록에서 고른 행이 활성 #FFCCEFFE(파랑) 대신
        // #FFECECEC 로 그려져 배경과 구분되지 않았다 — 고른 것이 안 골라진 것처럼 보인다.
        // 캡처는 포커스를 옮겨 주지 않고, 빈 곳을 누르는 것으로는 포커스가 들어오지 않는다
        // (항목을 직접 클릭할 때는 WPF 가 준다 — 그래서 그 경로만 멀쩡했다).
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

        // Esc 는 창에서 받는다 — 캡처가 키보드를 옮겨 주지 않는다.
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

    /// <summary>
    /// 고무줄을 끝낸다. <b>골라진 선택은 그대로 둔다</b> — 그 상태에서 드래그·복사·삭제가 이어진다.
    /// </summary>
    /// <remarks>
    /// <see cref="MouseUp"/> · <see cref="UIElement.LostMouseCapture"/> · <c>Esc</c> 세 경로가 전부
    /// 이것을 부른다. <b>상태를 먼저 비운다</b> — <c>ReleaseMouseCapture</c> 가
    /// <c>LostMouseCapture</c> 로 여기 다시 들어오기 때문이다(배치 편집의 <c>EndDrag</c> 와 같은 결).
    /// 임계를 못 넘고 끝난 경우도 이 함수가 받는다 — 그때는 걸어 둔 것이 없어 비우기만 한다.
    /// </remarks>
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

    /// <summary>
    /// 사각형과 겹치는 항목을 고른다 — <b>선택은 WPF 것을 그대로 쓴다.</b>
    /// </summary>
    /// <remarks>
    /// <c>SelectedItems</c> 만 갱신하고 뷰모델에는 기존 <c>OnSelectionChanged</c> 통로가 옮겨
    /// 적는다 — 새 선택 통로를 만들지 않고 <c>SelectionMode</c> 도 건드리지 않는다.
    /// <para>
    /// <b>실현된 컨테이너만 본다.</b> 가상화가 켜져 있어(<c>Recycling</c>) 화면 밖 항목에는
    /// 사각형이 없다 — 없는 것을 억지로 만들지 않는다(자동 스크롤이 v1 제외라 이것이 일관된다).
    /// </para>
    /// <para>
    /// 달라진 것만 넣고 뺀다. 매번 비우고 다시 채우면 <c>SelectionChanged</c> 가 폭주하고
    /// 목록이 깜빡인다.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 지금 <b>화면에 있는</b> 행과 그 사각형 (<c>ListArea</c> 좌표).
    /// </summary>
    /// <remarks>
    /// 가상화 때문에 <see cref="ItemContainerGenerator.ContainerFromIndex"/> 는 화면 밖 항목에
    /// null 을 준다. 좌표계는 <c>ListArea</c> 하나로만 잰다 — 사각형과 같은 기준이어야 스크롤된
    /// 목록에서 어긋나지 않는다.
    /// </remarks>
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

    // ── 컬럼 자동 폭 (§3.2 · Phase 5 Stage 3D-2) ──────────────────────────────
    //
    // 계산은 <see cref="ColumnLayout"/> 에 있고(뷰 밖에서 시험할 수 있게) 여기 남는 것은
    // <b>언제 다시 쓰는가</b>뿐이다. 문턱이 둘이고 둘 다 필요하다 — 아래 주석 참고.

    /// <summary>마지막으로 폭을 써 준 컬럼. 뷰를 갈아타면 다른 인스턴스가 온다.</summary>
    private GridViewColumn? _flexColumn;

    /// <summary>그때 써 준 값. 같은 값을 다시 쓰지 않는다(재진입 차단).</summary>
    private double _flexWidth = double.NaN;

    /// <summary>
    /// 목록의 스크롤 정보가 바뀌었다 — 늘리는 컬럼의 폭을 다시 잡는다.
    /// </summary>
    /// <remarks>
    /// <b>기준은 <c>ActualWidth</c> 가 아니라 <c>ScrollViewer.ViewportWidth</c> 다</b>(실측: 폭 444
    /// 목록에서 세로 스크롤바가 있으면 427, 없으면 444). <c>ActualWidth</c> 를 쓰면 스크롤바가 뜬
    /// 만큼 컬럼이 넘쳐 가로 스크롤이 생기고, 그 가로바가 세로 공간을 먹어 세로바 유무를 다시
    /// 흔든다 — 그것이 진동 고리다. <c>ScrollChanged</c> 는 <b>레이아웃 뒤</b>에 오므로 이 값이
    /// 이미 스크롤바를 반영한 폭이다.
    /// <para>
    /// <b>문턱 ①: 뷰포트 폭이 안 바뀌었으면 손대지 않는다.</b> 그래야 사용자가 머리글을 끌어
    /// 조절한 폭을 그 자리에서 덮어쓰지 않는다(끄는 것은 <c>ExtentWidth</c> 만 바꾼다). v1 은
    /// 자동 폭만이므로 <b>다음 크기 변화에는 덮인다</b> — 막지도 저장하지도 않는다(§3-4).
    /// </para>
    /// <para>
    /// <b>문턱 ②: 계산값이 지금 값과 같으면 쓰지 않는다.</b> 쓰면 레이아웃이 다시 돌고 그것이
    /// 다시 이 핸들러를 부른다 — 재진입이 여기서 끊긴다. 실측(스파이크): 폭을 1px 씩 61단계
    /// 옮기며 단계당 쓰기 최대 1회, 폭을 고정하면 추가 0회.
    /// </para>
    /// <para>
    /// 뷰를 갈아타면(평소 ↔ 재귀 검색) <b>컬럼 인스턴스가 새것</b>이라 뷰포트가 그대로여도 한 번
    /// 다시 쓴다 — 그러지 않으면 검색 뷰가 XAML 출발값(위치 180)으로 남는다.
    /// </para>
    /// </remarks>
    private void OnDetailsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ListView { View: GridView grid }
            || ColumnLayout.FlexColumn(grid) is not { } column)
        {
            return;
        }

        var swapped = !ReferenceEquals(column, _flexColumn);

        if (!swapped && e.ViewportWidthChange == 0)
        {
            return;
        }

        // 아직 폭이 잡히지 않았다 — 최소 폭으로 눌러 놓으면 첫 그림에서 한 번 튄다.
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
    /// 항목 우클릭 — <b>셸 메뉴를 그대로 띄운다</b> (§4.6 · D32 상시 병합).
    /// </summary>
    /// <remarks>
    /// <b>버튼을 놓을 때 띄운다.</b> 선택을 잡는 것은 누를 때
    /// (<see cref="OnItemPreviewRightButtonDown"/>)이고 — 순서가 뒤집히면 무엇이 대상인지가
    /// 메뉴가 뜬 뒤에 정해진다 — 메뉴는 그 선택이 목록에 보인 뒤에 뜬다.
    /// <para>
    /// 대상은 <b>고른 것 전부</b>다. 누를 때 선택을 잡아 주므로 선택 밖 항목을 우클릭했으면
    /// 이 시점에 이미 그것 하나만 골라져 있다.
    /// </para>
    /// <para>
    /// 메뉴가 떠 있는 동안 UI 가 멈추는 것은 감수한 대가다(조사 실측 70~90ms · 프로세스
    /// 첫 호출 532.7ms). COM STA 라 백그라운드로 뺄 수 없다.
    /// </para>
    /// </remarks>
    private void OnItemPreviewRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem { DataContext: FileItemViewModel item }
            || ViewModel is not { } panel
            || PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        // 우클릭한 항목이 선택 안에 없으면(=선택 잡기가 안 걸린 경로) 그것 하나만 대상이다.
        var chosen = panel.SelectedItems.Count > 0 && panel.SelectedItems.Contains(item)
            ? panel.SelectedItems
            : [item];

        var paths = chosen.Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        e.Handled = true;

        // 셸에 넘기기 전에 막는다 (F-05). GetUIObjectOf 는 한 폴더의 자식 PIDL 만 받으므로
        // 재귀 검색 결과에서 여러 폴더를 섞어 고른 것은 띄우지 않고 거절한다 — 그대로 넘기면
        // 셸이 거절하거나 같은 이름의 엉뚱한 파일에 메뉴가 걸린다.
        if (!ShellMenuPolicy.SharesOneParent(paths))
        {
            panel.ReportShellMenuRejection("셸 메뉴는 한 폴더 안에서 고른 것에만 — 지금 여러 폴더가 섞였다");
            return;
        }

        var screen = PointToScreen(e.GetPosition(this));

        try
        {
            var outcome = ShellItemMenu.Show(source, paths, item.IsDirectory, (int)screen.X, (int)screen.Y);

            if (outcome.ShellHr != 0)
            {
                panel.ReportShellMenuFailure($"메뉴 항목을 실행하지 못했다 — 0x{outcome.ShellHr:X8}");
            }

            RunAppCommand(panel, item, outcome.App);
        }
        catch (Exception ex)
        {
            // 확장이 던진 것을 우리 문구로 덮지 않는다 — 그대로 상태 바에 올린다(§5).
            panel.ReportShellMenuFailure($"우클릭 메뉴를 띄우지 못했다 — {ex.Message}");
        }
    }

    /// <summary>
    /// 셸 메뉴에 끼워 둔 앱 항목 다섯을 <b>기존 커맨드</b>로 잇는다 (§4.6 · D34).
    /// </summary>
    /// <remarks>
    /// 새 커맨드를 만들지 않는다 — 옛 <c>ItemMenu</c> 가 바인딩으로 부르던 그 커맨드 그대로다.
    /// 대상도 그때와 같다: 넷은 우클릭한 항목 하나, 「휴지통으로 삭제」만 <b>고른 것 전부</b>라
    /// 인자가 없다.
    /// <para>
    /// <c>TrashSelectionCommand</c>·<c>RegisterFolderCommand</c> 는 패널이 아니라 타일이 걸어
    /// 주는 통로라 <c>null</c> 일 수 있다 — 안 걸렸으면 조용히 지난다(옛 메뉴도 그랬다).
    /// </para>
    /// </remarks>
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
        }
    }

    // ── 끌어온 것 받기 (§4.9 Phase 5 Stage 1 · §4.2-2 Phase 5 Stage 3) ────────
    //
    // Stage 1 이 타일을 셸 드롭의 과녁으로 만들었고, Stage 3 이 여기에 <b>분기 하나</b>를 더했다 —
    // 앱 안에서 시작한 드래그(§4.2-2)도 타일로 놓을 수 있다. 타일에서 타일로 파일을 끌어 옮기는
    // 것이 이 경로다. 목적지는 셸 드롭과 같게 타일의 <c>CurrentPath</c> 이고 앵커가 아니다.

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
    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        // 받지 않으면 과녁을 켜지 않고 커서도 "안 됨"이다 (§4.2-2).
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

        // 어느 분기로 갔는지 남긴다 — 받지 않은 드롭도 이유가 로그에 보여야 한다.
        DropRouter.TraceDrop(
            "tile", DropRouter.Route(e.Data), drop?.Destination, drop?.Items.Count ?? 0);

        if (drop is not { } accepted)
        {
            return;
        }

        e.Handled = true;

        // 조작 실패는 결과로 보고되므로 여기까지 예외로 올라오지 않는다 (§5).
        await accepted.Host.DropOntoPathAsync(
            accepted.Destination, accepted.Items, accepted.Copy);
    }

    /// <summary>
    /// 받을 수 있는 드롭이면 (통로, 목적지, 항목, 복사여부). 아니면 null. 과녁 강조도 여기서
    /// 켠다 — 판정과 강조가 갈라지면 "파랗게 켜졌는데 안 받는" 자리가 생긴다.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> 와 <c>Drop</c> 이 <b>이 함수 하나</b>를 쓴다. 포맷 순서와 수정키 규칙은
    /// <see cref="DropRouter.Resolve"/> 에 있어 왼쪽 폴더 행·대상함과 같은 것을 본다 — 여기서
    /// 더하는 것은 <b>목적지</b>뿐이고, 타일의 목적지는 앵커가 아니라 지금 보고 있는 경로다(§4.9).
    /// 그 목적지는 <c>Resolve</c> 에 넘긴다 — 볼륨 판정이 그것을 봐야 한다(§4.2-2).
    /// <para>
    /// 같은 타일 안에 놓거나 두 타일이 같은 폴더를 볼 때는 <b>막을 것이 없다</b> — 항목의 부모가
    /// 목적지와 같아 ①(<c>AcceptedForDrop</c>)에 전부 걸려 0개가 되고, 그래서 과녁이 켜지지
    /// 않는다. 반대로 <b>재귀 검색 결과를 자기 타일에 놓으면 받는다</b> — 하위 폴더 항목의 부모는
    /// 목적지와 다르다. §4.2-2 가 적어 둔 동작이라 막지 않는다.
    /// </para>
    /// </remarks>
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

        // 문구도 여기서 정한다 — 판정을 낸 자리와 갈라지면 "복사라고 써 놓고 옮기는" 자리가 생긴다.
        panel.DropTargetCopies = incoming.Copy;
        return (host, destination, incoming.Items, incoming.Copy);
    }

    /// <summary>
    /// 마우스 뒤로/앞으로 버튼. 목록 안에서 눌러도 듣도록 터널링 단계에서 받는다 —
    /// 자식이 먼저 처리해 버리면 놓치기 때문이다.
    /// <para>
    /// <b>왼쪽 버튼은 이 패널에 키보드 포커스를 준다</b> (Phase 5 「타일 포커스」 C-1).
    /// 같은 터널링 자리를 쓰는 이유는 그 아래 분기 주석에 있다.
    /// </para>
    /// </summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        switch (e.ChangedButton)
        {
            case MouseButton.Left:
                // 여백 다운으로는 WPF 가 포커스를 주지 않아 패널 층 InputBindings 일곱
                // (Alt+Home·Alt+↑·Alt+←·Alt+→·F2·Ctrl+C·Ctrl+V)이 전부 죽었다 — 그 일곱은
                // 이 패널 안에 포커스가 있어야 발동한다. 항목을 직접 클릭할 때만 WPF 가
                // 컨테이너에 포커스를 줘서 그 경로만 멀쩡했다.
                //
                // 가드가 없으면 항목·검색창의 포커스를 빼앗는다. 이 핸들러는 터널링
                // (Preview) 단계라 ListViewItem·SearchBox 보다 먼저 오므로, 이미 패널 안에
                // 들어와 있는 포커스도 매 다운마다 패널 자신으로 끌어올려 버린다.
                //
                // e.Handled 는 세우지 않는다 — 이후 경로(항목 선택·고무줄·버튼·TextBox·
                // ContextMenu)가 그대로 돌아야 한다. 그것이 이 한 줄의 유일한 안전장치다.
                if (!IsKeyboardFocusWithin)
                {
                    Focus();
                }

                break;

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
