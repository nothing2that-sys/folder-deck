using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

/// <summary>
/// 검색 타일에서는 「지금 보고 있는 폴더」 메뉴의 항목이 전부 대상이 없다 (§4.6 · §4.12 ·
/// D75-b-3·6-5). 판정은 <c>IsSearchTile</c> 을 직접 본다 — <c>CurrentPath == null</c> 부작용에
/// 기대지 않는다.
/// </summary>
public sealed class CurrentFolderMenuSearchGuardTests
{
    /// <summary>패널만 떼어 만든다 — <see cref="ClipboardCopyPasteTests"/> 의 <c>Bare</c> 와 같은 결.</summary>
    private static FolderPanelViewModel BareSearchPanel(List<string> rejections) => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: rejections.Add);

    /// <summary>검색 타일에서 「새 폴더 만들기」가 거절된다.</summary>
    // 12
    [Fact]
    public void CreateNewFolderIsRejectedOnASearchTile()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);

        panel.CreateNewFolderCommand.Execute("새 폴더");

        Assert.Single(rejections);
    }

    /// <summary>검색 타일에서 「이 폴더를 앵커로 지정」이 거절된다.</summary>
    // 13
    [Fact]
    public async Task SetAnchorToCurrentIsRejectedOnASearchTile()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);
        var invoked = false;
        panel.SetAnchorFolderToPath = (_, _) =>
        {
            invoked = true;
            return true;
        };

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        Assert.Single(rejections);
        Assert.False(invoked);
    }

    /// <summary>
    /// 가드가 <c>IsSearchTile</c> 을 본다 — <c>CurrentPath</c> 에 값이 있어도(검색 타일이
    /// 아니었다면 통과했을 값) 검색 타일이면 여전히 거절한다. <c>CurrentPath == null</c>
    /// 부작용이었다면 이 경우 통과했을 것이다.
    /// </summary>
    // 14
    [Fact]
    public void TheGuardChecksIsSearchTileNotCurrentPath()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);
        panel.CurrentPath = Path.Combine(Path.GetTempPath(), "이미-값이-있다");

        panel.RegisterCurrentFolderCommand.Execute(null);

        Assert.Single(rejections);
    }
}
