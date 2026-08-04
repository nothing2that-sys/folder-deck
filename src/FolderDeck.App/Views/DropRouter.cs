using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Operations;
using IoDirectory = System.IO.Directory;

namespace FolderDeck.App.Views;

/// <summary>한 드롭을 어느 규칙으로 처리할지 (§4.9 · Phase 5 Stage 1).</summary>
public enum DropRoute
{
    /// <summary>받을 수 있는 포맷이 없다.</summary>
    None,

    /// <summary>앱 안에서 시작한 드래그 — 기본 <b>이동</b> / <c>Ctrl</c> 복사 (§4.2-2).</summary>
    Internal,

    /// <summary>탐색기·바탕화면에서 온 드래그 — 기본 <b>복사</b> / <c>Shift</c> 이동 (§4.9).</summary>
    Shell,
}

/// <summary>
/// 받는 쪽의 갈림길. <b>타일 · 왼쪽 폴더 행 · 대상함 세 자리가 이 한 곳을 쓴다.</b>
/// </summary>
/// <remarks>
/// 같은 <c>if</c> 를 세 군데에 베껴 쓰면 하나만 고쳐지는 사고가 난다. <c>DragOver</c> 와
/// <c>Drop</c> 도 반드시 같은 순서를 봐야 한다 — 한쪽만 바꾸면 과녁은 켜지는데 처리되지
/// 않거나 그 반대가 된다.
/// </remarks>
public static class DropRouter
{
    /// <summary>
    /// 어느 규칙으로 처리할지. <b>사설 포맷을 먼저 본다.</b>
    /// </summary>
    /// <remarks>
    /// 한 드롭에 사설 포맷과 셸 포맷이 <b>함께 실려 있을 수 있다</b> — 내보내기(§4.9, Stage 2)를
    /// 만들면 앱 안에서 시작한 드래그가 그렇게 된다. 그때 두 분기가 같은 드롭을 둘 다 물면
    /// 파일이 <b>두 번 처리된다.</b> 그래서 사설 포맷이 있으면 내부 규칙으로 처리하고 그 자리에서
    /// 끝낸다 — 셸 분기로 내려가지 않는다. 사설 포맷이 없을 때만 <c>FileDrop</c> 을 본다.
    /// <para>
    /// 두 규칙이 <b>일부러 반대</b>라(내부 = 기본 이동, 셸 = 기본 복사) 순서가 뒤집히면 앱이
    /// 관리하지 않는 폴더의 원본이 지워진다 — §4.9 가 막으려던 바로 그 사고다.
    /// </para>
    /// </remarks>
    public static DropRoute Route(IDataObject? data)
    {
        if (data is null)
        {
            return DropRoute.None;
        }

        if (data.GetDataPresent(FolderPanelView.FileDragFormat))
        {
            return DropRoute.Internal;
        }

        return data.GetDataPresent(DataFormats.FileDrop) ? DropRoute.Shell : DropRoute.None;
    }

    /// <summary>
    /// 셸이 준 경로 목록을 엔진 인자로. <b>변환 계층이 없다</b>(§4.9) — 경로마다 폴더인지만
    /// 디스크에 묻는다.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> 마다 불리므로 매번 <c>HDROP</c> 을 마샬링하고 항목 수만큼
    /// <see cref="IoDirectory.Exists"/> 를 부른다. <b>기억해 두지 않는다</b> — 파일 3000개를
    /// 끌고 다녀도 버벅임이 없다는 것을 손으로 확인했고(Stage 1 손 확인 7c), 재 보지 않은
    /// 최적화를 남기지 않는다.
    /// </remarks>
    public static IReadOnlyList<OperationItem> ShellItems(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths
            ? [.. paths.Select(p => new OperationItem(p, IoDirectory.Exists(p)))]
            : [];

    /// <summary>
    /// 셸 드롭의 수정키 — <b>기본 복사, <c>Shift</c> 를 누르면 이동</b> (§4.9).
    /// </summary>
    /// <remarks>
    /// 내부 규칙(기본 이동 / <c>Ctrl</c> 복사)과 <b>일부러 반대다.</b> 앱 안의 드래그는 이미 이
    /// 워크스페이스에 속한 파일을 옮기는 것이지만, 밖에서 들어오는 것은 남의 폴더에 있는
    /// 원본이다. 확인창이 없는 경로에서 기본값이 이동이면 한 번의 드래그가 앱이 관리하지 않는
    /// 폴더의 파일을 지운다.
    /// <para>판정은 <b>놓는 순간</b>의 <c>KeyStates</c> 로 한다 — 끌다가 마음을 바꿀 수 있어야 한다.</para>
    /// </remarks>
    public static bool ShellDropCopies(DragDropKeyStates keys) =>
        (keys & DragDropKeyStates.ShiftKey) != DragDropKeyStates.ShiftKey;

    /// <summary>
    /// 앱 안에서 시작한 드래그의 수정키 — <b>기본 이동, <c>Ctrl</c> 을 누르면 복사</b> (§4.2-2).
    /// </summary>
    /// <remarks>
    /// <see cref="ShellDropCopies"/> 와 <b>일부러 반대다.</b> 앱 안의 드래그는 이미 이
    /// 워크스페이스에 속한 파일을 옮기는 것이라 이동이 기본이지만, 밖에서 들어오는 것은 남의
    /// 폴더에 있는 원본이다.
    /// <para>판정은 <b>놓는 순간</b>의 <c>KeyStates</c> 로 한다 — 끌기 시작할 때가 아니다.</para>
    /// </remarks>
    public static bool InternalDropCopies(DragDropKeyStates keys) =>
        (keys & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

    /// <summary>
    /// 한 드롭을 <b>항목 목록 + 복사여부</b>로 푼다 — 받을 포맷이 없으면 null (§4.9 · §4.2-2).
    /// </summary>
    /// <remarks>
    /// <b>여기가 두 규칙이 갈리는 유일한 자리다.</b> <see cref="Route"/> 가 정한 순서 위에
    /// 포맷 꺼내기와 수정키 규칙을 얹는다 — 내부는 기본 이동(<c>Ctrl</c> 복사), 셸은 기본
    /// 복사(<c>Shift</c> 이동)로 일부러 반대다.
    /// <para>
    /// <b>목적지는 여기서 정하지 않는다.</b> 자리마다 다르기 때문이다 — 왼쪽 폴더 행과 대상함은
    /// 앵커 경로(<c>Entry.Path</c>)이고 타일은 지금 보고 있는 경로(<c>CurrentPath</c>)다(§4.9).
    /// 목적지가 정해진 뒤의 ①②③ 판정은 <c>MainViewModel.CanDropOnto</c> 한 곳이 한다.
    /// </para>
    /// </remarks>
    public static (IReadOnlyList<OperationItem> Items, bool Copy)? Resolve(
        IDataObject? data, DragDropKeyStates keys)
    {
        switch (Route(data))
        {
            case DropRoute.Internal:
                // 앱 안에서 시작한 드래그 — 사설 포맷에 항목이 이미 실려 있어 변환이 없다.
                return data!.GetData(FolderPanelView.FileDragFormat) is FileDropPayload payload
                    ? (payload.Items, InternalDropCopies(keys))
                    : null;

            case DropRoute.Shell:
                return (ShellItems(data!), ShellDropCopies(keys));

            default:
                return null;
        }
    }

    /// <summary>
    /// 커서에 돌려줄 효과 — <b>우리가 정한 하나로 좁힌다.</b> 셸은 <c>Copy, Move, Link</c> 를
    /// 다 허용하지만(Stage 0 실측) <b><c>Link</c> 는 쓰지 않는다</b>(§4.9).
    /// </summary>
    public static DragDropEffects Effect(bool copy) =>
        copy ? DragDropEffects.Copy : DragDropEffects.Move;

    /// <summary>셸 드롭의 규칙과 효과를 이어 붙인 것 — 기본 복사, <c>Shift</c> 면 이동.</summary>
    public static DragDropEffects ShellEffect(DragDropKeyStates keys) =>
        Effect(ShellDropCopies(keys));

    /// <summary>
    /// 어느 분기로 갔는지 남긴다 — <c>early return</c> 을 조용히 하지 않는다. 두 포맷이 함께
    /// 실린 드롭(내보내기가 생기면 그렇게 된다)이 실제로 어느 규칙을 탔는지는 이 줄로 확인한다.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> 에서는 부르지 않는다 — 마우스가 움직이는 동안 초당 수십 줄이 쌓여
    /// 정작 봐야 할 <c>Drop</c> 한 줄이 묻힌다.
    /// </remarks>
    public static void TraceDrop(string site, DropRoute route, string? destination, int itemCount) =>
        Trace.WriteLine(
            $"[FolderDeck] drop site={site} route={route} items={itemCount} " +
            $"dest={destination ?? "(없음)"}");
}
