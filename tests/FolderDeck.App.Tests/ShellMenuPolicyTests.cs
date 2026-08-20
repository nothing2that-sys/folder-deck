using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;

/// <summary>
/// 셸 컨텍스트 메뉴 상시 병합의 <b>판정</b> (§4.6 · D32~D34).
/// </summary>
/// <remarks>
/// COM interop 자체는 여기서 못 덮는다 — <c>IContextMenu</c> 는 STA 를 요구하고 이 테스트
/// 프로젝트는 MTA 다. 덮는 것은 <see cref="ShellMenuPolicy"/> 의 순수 판정 셋뿐이고,
/// 실제로 메뉴가 뜨는지·확장이 도는지는 손 확인(§10)의 일이다.
/// </remarks>
public sealed class ShellMenuPolicyTests
{
    // ── D33: 지우는 verb 는 delete · cut 둘뿐 ────────────────────────────────

    [Theory]
    [InlineData("delete")]
    [InlineData("cut")]
    public void RemovesDeleteAndCut(string verb)
    {
        Assert.True(ShellMenuPolicy.IsRemovedVerb(verb));
    }

    /// <summary>
    /// <c>link</c>(바로 가기 만들기)는 <b>남긴다</b> — 충돌이 없고 폴더 감시가 새 파일을 잡는다.
    /// </summary>
    [Theory]
    [InlineData("link")]
    [InlineData("open")]
    [InlineData("copy")]
    [InlineData("copyaspath")]
    [InlineData("properties")]
    [InlineData("paste")]
    public void KeepsEverythingElse(string verb)
    {
        Assert.False(ShellMenuPolicy.IsRemovedVerb(verb));
    }

    /// <summary>verb 를 못 얻으면(<c>null</c>) 지우지 않는다 — 모르는 것을 지우지 않는다.</summary>
    [Fact]
    public void KeepsItemWithoutVerb()
    {
        Assert.False(ShellMenuPolicy.IsRemovedVerb(null));
        Assert.False(ShellMenuPolicy.IsRemovedVerb(string.Empty));
    }

    /// <summary>
    /// verb 의 대소문자는 셸이 정한다 — <c>Delete</c> 도 <c>CUT</c> 도 같은 것이다.
    /// </summary>
    [Theory]
    [InlineData("Delete")]
    [InlineData("DELETE")]
    [InlineData("Cut")]
    [InlineData("CUT")]
    public void VerbMatchIsCaseInsensitive(string verb)
    {
        Assert.True(ShellMenuPolicy.IsRemovedVerb(verb));
    }

    // ── D34: 앱/셸 분기는 id 대역 하나로 갈린다 ──────────────────────────────

    /// <summary>앱 항목 다섯이 <b>각각 자기 커맨드로</b> 갈린다.</summary>
    [Theory]
    [InlineData(0x11u, ShellMenuAppCommand.RevealInExplorer)]
    [InlineData(0x12u, ShellMenuAppCommand.Rename)]
    [InlineData(0x13u, ShellMenuAppCommand.SetAnchor)]
    [InlineData(0x14u, ShellMenuAppCommand.RegisterFolder)]
    [InlineData(0x15u, ShellMenuAppCommand.Trash)]
    public void AppIdsMapToTheirOwnCommand(uint id, ShellMenuAppCommand expected)
    {
        Assert.Equal(expected, ShellMenuPolicy.ToAppCommand(id));
    }

    /// <summary>
    /// <c>idCmdFirst</c>(0x1000) 이상은 셸 것이다 — 앱 커맨드로 새지 않는다.
    /// </summary>
    [Theory]
    [InlineData(0x1000u)]
    [InlineData(0x1001u)]
    [InlineData(0x7FFFu)]
    public void ShellIdsAreNotAppCommands(uint id)
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(id));
    }

    /// <summary>아무것도 안 골랐다(<c>TrackPopupMenuEx</c> 가 0 을 준다).</summary>
    [Fact]
    public void ZeroIsNothingChosen()
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(0));
    }

    /// <summary>대역 안이어도 우리가 넣지 않은 id 면 아무 커맨드도 부르지 않는다.</summary>
    [Theory]
    [InlineData(0x01u)]
    [InlineData(0x10u)]
    [InlineData(0x16u)]
    [InlineData(0x0FFFu)]
    public void UnknownAppBandIdRunsNothing(uint id)
    {
        Assert.Equal(ShellMenuAppCommand.None, ShellMenuPolicy.ToAppCommand(id));
    }

    // ── D34: 끼우는 항목은 다섯 (폴더) / 셋 (파일) ───────────────────────────

    /// <summary>폴더에는 다섯 전부가 그 순서로 들어간다.</summary>
    [Fact]
    public void FolderGetsAllFiveInOrder()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: true);

        Assert.Equal(
            [
                ShellMenuAppCommand.RevealInExplorer,
                ShellMenuAppCommand.Rename,
                ShellMenuAppCommand.SetAnchor,
                ShellMenuAppCommand.RegisterFolder,
                ShellMenuAppCommand.Trash,
            ],
            items.Select(i => i.Command));
    }

    /// <summary>
    /// 파일에는 폴더 전용 둘(앵커 지정·새 폴더로 등록)을 넣지 않는다 — 눌러도 아무 일 없는
    /// 항목을 남기지 않는다(옛 <c>ItemMenu</c> 가 <c>Visibility</c> 로 하던 규칙 그대로다).
    /// </summary>
    [Fact]
    public void FileSkipsTheFolderOnlyTwo()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: false);

        Assert.Equal(
            [
                ShellMenuAppCommand.RevealInExplorer,
                ShellMenuAppCommand.Rename,
                ShellMenuAppCommand.Trash,
            ],
            items.Select(i => i.Command));
    }

    /// <summary>
    /// 겹치는 넷(열기 · 복사 · 경로 복사 · 속성)은 앱 항목으로 넣지 않는다 — 셸 것을 쓴다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingFourAreNotInserted(bool isDirectory)
    {
        var texts = ShellMenuPolicy.AppItems(isDirectory).Select(i => i.Text).ToList();

        Assert.DoesNotContain(texts, t => t.StartsWith("열기", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("복사", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("경로 복사", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("속성", StringComparison.Ordinal));
    }

    /// <summary>
    /// 단축키 글자는 잃지 않는다 — 옛 <c>InputGestureText</c> 가 하던 표시다.
    /// 메뉴는 <c>\t</c> 뒤를 오른쪽에 정렬해 그린다.
    /// </summary>
    [Fact]
    public void KeyLabelsRideOnTheItemText()
    {
        var items = ShellMenuPolicy.AppItems(isDirectory: true);

        Assert.Equal("이름 바꾸기\tF2", items.Single(i => i.Command == ShellMenuAppCommand.Rename).Text);
        Assert.Equal("휴지통으로 삭제\tDel", items.Single(i => i.Command == ShellMenuAppCommand.Trash).Text);
    }

    /// <summary>
    /// 넣는 항목의 id 는 전부 셸 대역 <b>아래</b>다 — 그래야 겹치지 않고 갈릴 수 있다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryAppItemIdIsBelowTheShellBand(bool isDirectory)
    {
        foreach (var item in ShellMenuPolicy.AppItems(isDirectory))
        {
            Assert.Equal(item.Command, ShellMenuPolicy.ToAppCommand((uint)item.Command));
        }
    }

    // ── F-05: 셸 메뉴는 한 부모 아래에서 고른 것에만 ─────────────────────────

    /// <summary>
    /// 같은 폴더의 둘은 함께 넘길 수 있다 — 대소문자만 다른 것도 같은 폴더다
    /// (비교가 <c>OrdinalIgnoreCase</c> 라 Windows 와 어긋나지 않는다).
    /// </summary>
    [Fact]
    public void TwoInTheSameFolderShareOneParent()
    {
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\a\b\two.txt"]));
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\A\B\two.txt"]));
    }

    /// <summary>
    /// 서로 다른 폴더가 섞이면 거절한다 — <c>GetUIObjectOf</c> 는 그 폴더의 자식 PIDL 만 받는다.
    /// </summary>
    [Fact]
    public void TwoInDifferentFoldersDoNot()
    {
        Assert.False(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt", @"C:\a\c\two.txt"]));
    }

    /// <summary>하나면 언제나 한 부모다 — 우클릭 하나가 막히면 안 된다.</summary>
    [Fact]
    public void OneAlwaysSharesOneParent()
    {
        Assert.True(ShellMenuPolicy.SharesOneParent([@"C:\a\b\one.txt"]));
    }

    /// <summary>빈 목록은 <c>false</c> 다 — 띄울 대상이 없으면 띄우지 않는다.</summary>
    [Fact]
    public void EmptyListSharesNothing()
    {
        Assert.False(ShellMenuPolicy.SharesOneParent([]));
    }
}
