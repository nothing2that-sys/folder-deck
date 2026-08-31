using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FolderDeck.App.Views;

/// <summary>눌린 자리가 무엇이냐 — 고무줄 선택의 갈림 (§4.2-2 · Phase 5 Stage 3B).</summary>
public enum ListHitKind
{
    /// <summary>빈 곳. <b>고무줄을 시작한다.</b></summary>
    Empty,

    /// <summary>항목 위. <b>기존 드래그 경로다</b> — 고무줄은 손대지 않는다 (§4.2-2).</summary>
    Item,

    /// <summary>스크롤바. 고무줄도 드래그도 아니다 — 스크롤바에 그대로 넘긴다.</summary>
    None,
}

/// <summary>
/// 고무줄 선택의 <b>판정과 기하</b> (§4.2-2 · Phase 5 Stage 3B).
/// </summary>
/// <remarks>
/// 상태를 갖지 않고 WPF 요소를 만들지도 않는다 — <b>뷰 밖에서 시험할 수 있게</b> 이렇게 뺐다.
/// 붙잡고 있는 상태(누른 자리 · 캡처 · 시작 전 선택)는 <see cref="FolderPanelView"/> 것이다.
/// <para>
/// <b>두 뷰 방식(<c>GridView</c> 상세 · 아이콘 스타일 리스트, §6.1)이 이 한 곳을 쓴다.</b>
/// 뷰 방식마다 따로 쓰면 하나만 고쳐지는 사고가 난다.
/// </para>
/// </remarks>
public static class RubberBand
{
    /// <summary>
    /// 조상 사슬을 보고 갈린다. <b>스크롤바를 먼저 본다.</b>
    /// </summary>
    /// <remarks>
    /// 스크롤바 썸은 <see cref="ListView"/> 템플릿 <b>안</b>에 있다. 그래서 고무줄을 목록의
    /// 마우스 다운에 걸면 <b>썸을 누른 것도 빈 곳으로 보인다</b> — 순서를 뒤집으면 끌어야 할
    /// 스크롤이 고무줄로 먹힌다.
    /// <para>
    /// 사슬을 <b>두 번 훑는다.</b> 한 번에 훑으면 "누가 더 가까운가"가 답을 정하는데, 그것은
    /// 여기서 정할 규칙이 아니다 — 스크롤바가 사슬에 있으면 그것으로 끝이어야 한다.
    /// </para>
    /// <para>
    /// <b><see cref="ListHitKind.Item"/> 이면 아무것도 하지 않는다</b> — 그 자리는 컨테이너
    /// 핸들러(<c>OnItemPreviewMouseDown</c> → <c>DoDragDrop</c>)가 이미 처리한다. <c>GridView</c>
    /// 상세에서는 행이 가로로 늘어나 <b>이름 오른쪽 빈 자리도 항목</b>이다(2026-08-04 결정,
    /// 셀 안 좌표를 계산하지 않는다).
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>요소가 아니라 타입을 받는다</b> — 뷰 밖에서 시험하려면 그래야 한다. WPF 컨트롤은
    /// <b>만드는 것만으로도 STA 스레드를 요구하고</b>(실측: `InputManager..ctor` 에서
    /// `호출 스레드가 STA 여야 합니다`) 테스트는 MTA 로 돈다. 타입은 만들지 않아도 있다.
    /// <para>
    /// <c>is</c> 와 같게 <b>파생 타입도 잡는다</b>(<see cref="Type.IsAssignableFrom"/>).
    /// </para>
    /// </remarks>
    public static ListHitKind Classify(IReadOnlyList<Type> ancestors)
    {
        ArgumentNullException.ThrowIfNull(ancestors);

        if (ancestors.Any(t => Is<ScrollBar>(t) || Is<Thumb>(t)))
        {
            return ListHitKind.None;
        }

        return ancestors.Any(Is<ListViewItem>) ? ListHitKind.Item : ListHitKind.Empty;

        static bool Is<T>(Type candidate) => typeof(T).IsAssignableFrom(candidate);
    }

    /// <summary>
    /// <paramref name="source"/> 부터 <paramref name="stopAt"/> 까지 조상들의 <b>타입</b>
    /// (자기 자신 포함).
    /// </summary>
    /// <remarks>
    /// 시각 트리로 오르되 시각 트리에 없는 것(<see cref="System.Windows.Documents.Run"/> 같은
    /// 콘텐츠 요소)은 논리 부모로 넘어간다 — <c>e.OriginalSource</c> 가 그런 것으로 올 수 있다.
    /// <paramref name="stopAt"/> 자신은 담지 않는다.
    /// <para>
    /// <b>이 훑기는 단위 테스트로 덮이지 않는다</b> — 실제 시각 트리가 있어야 하고 그것은 STA
    /// 스레드를 요구한다. 요소가 아닌 입력만 덮고 나머지는 손 확인이다(§7).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Type> AncestorTypes(object? source, DependencyObject? stopAt)
    {
        var chain = new List<Type>();

        for (var node = source as DependencyObject; node is not null && node != stopAt;)
        {
            chain.Add(node.GetType());
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return chain;
    }

    /// <summary>두 점이 만드는 사각형. 어느 방향으로 끌어도 너비·높이가 음수가 되지 않는다.</summary>
    public static Rect Between(Point a, Point b) => new(a, b);

    /// <summary>
    /// 누른 자리에서 <b>임계를 넘겼나</b> — 못 넘겼으면 고무줄이 아니라 그냥 클릭이다 (§4-6).
    /// </summary>
    /// <remarks>
    /// 기존 항목 드래그와 <b>같은 <see cref="SystemParameters"/></b> 를 쓴다 — 새 숫자를 만들지
    /// 않는다. 두 축 다 미달일 때만 거짓이다(= 어느 한 축이라도 넘으면 참).
    /// </remarks>
    public static bool PastThreshold(Point origin, Point now) =>
        Math.Abs(now.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(now.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>
    /// 고무줄을 <b>시작하는 순간</b> 지켜야 할 기존 선택 (§3).
    /// </summary>
    /// <remarks>
    /// <c>Ctrl</c> 이면 기존 선택에 <b>더하고</b>(탐색기 관용), 아니면 <b>지운다.</b> 판정 시점이
    /// 시작하는 순간인 이유: 끌던 도중에 <c>Ctrl</c> 을 떼면 이미 지운 것을 되살릴 수가 없다 —
    /// 내부 드롭이 "놓는 순간"으로 보는 것과 <b>일부러 다르다.</b>
    /// </remarks>
    public static IReadOnlyList<object> Kept(bool adds, IReadOnlyList<object> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        return adds ? [.. selected] : [];
    }

    /// <summary>
    /// 이 사각형이 고른 것 = <paramref name="kept"/> + 사각형과 겹치는 항목.
    /// </summary>
    /// <remarks>
    /// <paramref name="rows"/> 는 <b>실현된 컨테이너만</b> 들어온다 — 가상화가 켜져 있어
    /// (<c>Recycling</c>) 화면 밖 항목에는 사각형이 없고, 없는 것을 억지로 만들지 않는다
    /// (자동 스크롤이 v1 제외라 이것이 일관된다). 목록이 비어 있으면 <paramref name="kept"/> 만
    /// 남는다 — 예외 없이.
    /// </remarks>
    public static HashSet<object> Wanted(
        IReadOnlyList<object> kept, IEnumerable<(object Item, Rect Bounds)> rows, Rect band)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(rows);

        var wanted = new HashSet<object>(kept);

        foreach (var (item, bounds) in rows)
        {
            if (Touches(band, bounds))
            {
                wanted.Add(item);
            }
        }

        return wanted;
    }

    /// <summary>
    /// 사각형과 항목이 <b>겹치는가</b> — 완전히 감싸지 않아도 고른다(§3 의 정한 규칙).
    /// </summary>
    /// <remarks>
    /// <b>맞닿은 것은 겹침으로 보지 않는다</b>(경계가 열려 있다) — 겹친 면적이 0 이면 겹친 것이
    /// 아니다. <c>Rect.IntersectsWith</c> 를 쓰지 않은 이유가 이것이다: 그것은 맞닿은 것도
    /// 참으로 준다. <b>이것은 이 단계가 정한 것이고 탐색기를 실측한 것이 아니다.</b>
    /// </remarks>
    public static bool Touches(Rect band, Rect item) =>
        band.Left < item.Right && band.Right > item.Left
        && band.Top < item.Bottom && band.Bottom > item.Top;
}

/// <summary>
/// 항목 드래그의 <b>임계 판정</b> (§4.2-2 · D71). 원래 <c>OnItemPreviewMouseMove</c> 안에
/// 박혀 있던 거리 판정을 그대로 뺐다.
/// </summary>
/// <remarks>
/// <see cref="RubberBand.PastThreshold"/> 와 같은 <see cref="SystemParameters"/> 값을 쓰지만
/// (기존 드래그와 새 임계값을 만들지 않는다는 결정 그대로다) 뜻이 다른 판정이라 —
/// 하나는 고무줄을 시작할지, 이것은 항목 드래그를 시작할지를 묻는다 — <see cref="RubberBand"/>
/// 안에 얹지 않고 같은 파일에 나란히 둔다. 원본 식도 <see cref="RubberBand.PastThreshold"/> 처럼
/// "어느 한 축이라도 넘으면 참"이 아니라 <b>"가로·세로 각각 비교해 둘 다 미달일 때만 아직
/// 클릭"</b>이라는 부정형 그대로다 — 글자 그대로 옮기라는 지시를 그렇게 지켰다.
/// </remarks>
public static class ItemDragThreshold
{
    /// <summary>시작점과 현재점 사이가 임계를 넘었나 — 넘지 않았으면 아직 클릭이다.</summary>
    public static bool PastThreshold(Point origin, Point now) =>
        !(Math.Abs(now.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
          && Math.Abs(now.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance);
}
