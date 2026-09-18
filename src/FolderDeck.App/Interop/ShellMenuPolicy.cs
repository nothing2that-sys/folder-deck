namespace FolderDeck.App.Interop;

/// <summary>
/// 셸 메뉴에 끼우는 앱 고유 항목 (§4.6 · D34). <b>값이 곧 메뉴 id 다.</b>
/// </summary>
/// <remarks>
/// 전부 <see cref="ShellContextMenu.IdCmdFirst"/>(0x1000) <b>아래</b>다 — 셸에는 그 위 대역을
/// 주므로 두 대역이 겹칠 수 없고, 돌아온 id 하나로 앱 것인지 셸 것인지 갈린다.
/// 이 방식은 조사(B-0)에서 실제로 넣고 눌러 <c>id=17</c> 반환까지 확인한 것이다.
/// </remarks>
public enum ShellMenuAppCommand : uint
{
    /// <summary>앱 항목이 아니다 — 셸 것이거나 아무것도 안 골랐다.</summary>
    None = 0,

    RevealInExplorer = 0x11,
    Rename = 0x12,
    SetAnchor = 0x13,
    RegisterFolder = 0x14,
    Trash = 0x15,

    /// <summary>내용 비교(기능 후보 ①의 연장, 2026-09-18) — 타일 비교 중 [다름] 표시된 파일에만.</summary>
    CompareContent = 0x16,
}

/// <summary>메뉴에 넣을 앱 항목 하나 — 보이는 글자와 그 id.</summary>
public readonly record struct ShellMenuAppItem(ShellMenuAppCommand Command, string Text);

/// <summary>
/// 셸 메뉴를 우리 것으로 만드는 <b>판정만</b> 모은 자리 (§4.6 · D33 · D34).
/// </summary>
/// <remarks>
/// COM 도 HMENU 도 여기 없다 — 그래서 UI 스레드 없이 시험할 수 있다. 실제로 메뉴를 만들고
/// 띄우는 것은 <see cref="ShellItemMenu"/> 이고, 그쪽은 이 판정을 <b>부르기만</b> 한다.
/// </remarks>
public static class ShellMenuPolicy
{
    /// <summary>
    /// 셸 메뉴에서 <b>지워야 하는</b> verb 인가 (D33).
    /// </summary>
    /// <remarks>
    /// <c>delete</c>(영구 삭제)를 남기면 「앱에 영구 삭제 경로 자체가 없다」(§4.4)가 깨진다.
    /// <c>cut</c> 은 붙여넣기가 이동을 처리하게 되면서 되살렸다(D33 개정 · D35).
    /// <para>
    /// <b>verb 로만 판정한다</b> — 지역화된 항목 이름(「삭제(&amp;D)」·「잘라내기(&amp;T)」)으로 찾으면
    /// 다른 언어에서 깨진다. <c>link</c>(바로 가기 만들기)는 충돌이 없어 남긴다.
    /// </para>
    /// </remarks>
    public static bool IsRemovedVerb(string? verb) =>
        string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 고른 것들이 <b>한 부모 아래</b>에 있는가 — 셸 메뉴를 띄울 수 있는 조건이다 (F-05).
    /// </summary>
    /// <remarks>
    /// <c>IShellFolder.GetUIObjectOf</c> 는 <b>그 폴더의 자식 PIDL</b> 만 받는다. 서로 다른 폴더의
    /// 항목을 섞어 넘기면 계약 위반이고, 셸이 거절하거나 <b>같은 이름의 엉뚱한 파일</b>에 메뉴가
    /// 걸린다. 비교는 COM 이 아니라 상위 디렉터리 문자열로 한다 — 여기 오는 것은 전부 파일
    /// 시스템 경로다.
    /// <para><b>빈 목록은 false</b> 다. 띄울 대상이 없으면 띄우지 않는다.</para>
    /// </remarks>
    public static bool SharesOneParent(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        string? first = System.IO.Path.GetDirectoryName(paths[0]);
        if (string.IsNullOrEmpty(first))
        {
            return false;
        }

        for (int i = 1; i < paths.Count; i++)
        {
            if (!string.Equals(System.IO.Path.GetDirectoryName(paths[i]), first, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>TrackPopupMenuEx</c> 가 돌려준 id 가 앱 항목인가 — <b>앱/셸 분기는 이 함수 하나다.</b>
    /// </summary>
    /// <remarks>
    /// 같은 판정을 두 곳에 만들면 대역이 바뀔 때 한쪽만 고쳐진다.
    /// 아무것도 안 골랐으면(<c>0</c>) <see cref="ShellMenuAppCommand.None"/> 이다.
    /// </remarks>
    public static ShellMenuAppCommand ToAppCommand(uint menuId) =>
        menuId != 0 && menuId < ShellContextMenu.IdCmdFirst && Enum.IsDefined(typeof(ShellMenuAppCommand), menuId)
            ? (ShellMenuAppCommand)menuId
            : ShellMenuAppCommand.None;

    /// <summary>
    /// 셸 항목 <b>앞에</b> 넣을 앱 항목들 (§4.6 · D34 — 기본 다섯/셋, 내용 비교가 걸리면 하나 더).
    /// </summary>
    /// <param name="isDirectory">우클릭한 항목이 폴더인가.</param>
    /// <param name="canCompareContent">
    /// 「내용 비교」를 끼울지 — 타일 비교(기능 후보 ①)가 켜져 있고 <b>이 항목이 [다름]으로
    /// 표시됐을 때만</b> 호출부(<c>FolderPanelView</c> 우클릭 핸들러)가 <c>true</c>
    /// 를 준다. 폴더에는 뜻이 없으므로 <paramref name="isDirectory"/> 가 참이면 이 값과
    /// 무관하게 넣지 않는다.
    /// </param>
    /// <remarks>
    /// 겹치는 넷(열기 <c>open</c> · 복사 <c>copy</c> · 경로 복사 <c>copyaspath</c> ·
    /// 속성 <c>properties</c>)은 넣지 않는다 — 셸 것을 쓴다. 커맨드는 그대로 살아 있다
    /// (<c>Ctrl+C</c> 등이 계속 쓴다).
    /// <para>
    /// 「이 폴더를 앵커로 지정」·「새 폴더로 등록」은 폴더에만 뜻이 있어 파일에는 넣지 않는다 —
    /// 눌러도 아무 일 없는 항목을 남기지 않는다(옛 <c>ItemMenu</c> 가 <c>Visibility</c> 로
    /// 하던 것과 같은 규칙이다). 「내용 비교」도 같은 규칙이다 — 항상 뜨는 항목이 아니라
    /// 조건이 맞을 때만 나타난다.
    /// </para>
    /// <para>
    /// 단축키 글자는 <c>\t</c> 뒤에 붙는다 — 메뉴가 오른쪽에 정렬해 그린다. 둘 다 실물이다:
    /// <c>F2</c> 는 패널이, <c>Del</c> 은 창이 실제로 물고 있다.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ShellMenuAppItem> AppItems(bool isDirectory, bool canCompareContent = false)
    {
        var items = new List<ShellMenuAppItem>(6)
        {
            new(ShellMenuAppCommand.RevealInExplorer, "탐색기에서 열기"),
            new(ShellMenuAppCommand.Rename, "이름 바꾸기\tF2"),
        };

        if (!isDirectory && canCompareContent)
        {
            items.Add(new(ShellMenuAppCommand.CompareContent, "내용 비교"));
        }

        if (isDirectory)
        {
            items.Add(new(ShellMenuAppCommand.SetAnchor, "이 폴더를 앵커로 지정"));
            items.Add(new(ShellMenuAppCommand.RegisterFolder, "새 폴더로 등록"));
        }

        items.Add(new(ShellMenuAppCommand.Trash, "휴지통으로 삭제\tDel"));
        return items;
    }
}
