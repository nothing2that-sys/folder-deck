using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 상세 뷰의 컬럼 머리를 눌러 정렬한다 (Phase 8-B ①). 그 전까지 정렬을 바꾸는 길은 헤더 메타의
/// 정렬 글자 메뉴 하나뿐이었다 — 탐색기를 쓰던 손은 머리글을 먼저 누른다.
/// </summary>
/// <remarks>
/// <b>여기서 시험하는 것은 머리글 <i>문구</i>다.</b> 누름 자체는 뷰(<c>OnColumnHeaderClick</c>)가
/// 받아 <see cref="FolderPanelViewModel.SetSortCommand"/> 로 넘기는데, 그 커맨드는
/// <see cref="SortEntryPointTests"/> 가 이미 덮고 있다 — 머리글이 새로 만든 규칙은 하나도 없다.
/// 남는 것은 "지금 어느 컬럼으로 어느 방향인지가 화면에 보이는가"이고 그것이 이 문구다.
/// </remarks>
public sealed class ColumnHeaderSortTests
{
    /// <summary>화살표는 <b>지금 정렬 중인 컬럼에만</b> 붙는다 — 셋 다 붙으면 아무 말도 안 하는 것이다.</summary>
    [Fact]
    public async Task OnlyTheSortedColumnCarriesTheArrow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(SortBy.Name, code.Entry!.SortBy);

        Assert.Equal("이름 ↑", code.NameHeaderText);
        Assert.Equal("크기", code.SizeHeaderText);
        Assert.Equal("수정", code.ModifiedHeaderText);
    }

    /// <summary>다른 컬럼으로 옮기면 화살표도 따라 옮긴다 — 옛 컬럼에 남으면 거짓말이 된다.</summary>
    [Fact]
    public async Task TheArrowMovesWithTheSortColumn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal("이름", code.NameHeaderText);
        Assert.Equal("크기", code.SizeHeaderText);
        Assert.Equal("수정 ↑", code.ModifiedHeaderText);
    }

    /// <summary>같은 컬럼을 다시 누르면 방향이 뒤집히고 화살표도 뒤집힌다.</summary>
    [Fact]
    public async Task ClickingTheSameColumnFlipsTheArrow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("크기 ↑", code.SizeHeaderText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.True(code.Entry!.SortDesc);
        Assert.Equal("크기 ↓", code.SizeHeaderText);
    }

    /// <summary>
    /// 머리글은 헤더 메타의 정렬 글자와 <b>같은 재료</b>에서 나온다 — 두 곳이 어긋날 수 없다.
    /// </summary>
    [Fact]
    public async Task TheHeaderAndTheMetaLineAgree()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        foreach (var sort in new[] { SortBy.Name, SortBy.Size, SortBy.Modified, SortBy.Size })
        {
            await code.SetSortCommand.ExecuteAsync(sort);

            var header = code.Entry!.SortBy switch
            {
                SortBy.Size => code.SizeHeaderText,
                SortBy.Modified => code.ModifiedHeaderText,
                _ => code.NameHeaderText,
            };

            Assert.Equal(code.SortLabel + code.SortArrow, header);
            Assert.Contains(code.SortLabel + code.SortArrow, code.MetaSortText);
        }
    }

    /// <summary>
    /// 통지가 같이 나가야 한다 — <c>[NotifyPropertyChangedFor]</c> 는 <c>entry</c> 필드에 붙어
    /// 있어서 같은 <c>Entry</c> 안의 값만 바꾸면 아무 통지도 안 나간다. 빠뜨리면 목록만 바뀌고
    /// 화살표가 옛 컬럼에 남는다.
    /// </summary>
    [Fact]
    public async Task NotificationsAccompanyTheHeaderChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SizeHeaderText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ModifiedHeaderText), seen);
    }

    /// <summary>
    /// 순환 칸이 다른 폴더로 갈아타면 그 폴더의 정렬로 머리글이 다시 선다 — 정렬은 폴더별이다.
    /// </summary>
    [Fact]
    public async Task SwitchingFoldersRedrawsTheHeaders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.Equal("이름 ↑", panel.NameHeaderText);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Equal("수정 ↑", panel.ModifiedHeaderText);

        // 다른 폴더로 갈아탄다 — 그 폴더는 이름 오름차순이다.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal("이름 ↑", panel.NameHeaderText);
        Assert.Equal("수정", panel.ModifiedHeaderText);
    }

    /// <summary>폴더를 안 띄운 순환 칸도 머리글은 그린다 — 화살표 없이 라벨만.</summary>
    [Fact]
    public void AnEmptyPanelStillHasPlainHeaders()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        Assert.Equal("이름", rotating.NameHeaderText);
        Assert.Equal("크기", rotating.SizeHeaderText);
        Assert.Equal("수정", rotating.ModifiedHeaderText);
    }
}
