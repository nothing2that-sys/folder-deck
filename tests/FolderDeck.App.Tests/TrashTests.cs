namespace FolderDeck.App.Tests;

/// <summary>쓰레기 정리 — 다중 선택 → 휴지통 (§4.4 · Phase 3 Stage 3).</summary>
public sealed class TrashTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    [Fact]
    public async Task EverySelectedItemGoesToTheRecycleBinAndTheCountIsAnnounced()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll", "b.dll");

        await f.ViewModel.TrashSelectionAsync();

        // 산출물 폴더는 크기 내림차순이라 보낸 순서는 목록 순서다 — 무엇을 보냈는지만 본다.
        Assert.Equal(
            [Path.Combine(f.OutputPath, "a.dll"), Path.Combine(f.OutputPath, "b.dll")],
            f.RecycleBin.Sent.Order());
        Assert.Empty(Directory.GetFiles(f.OutputPath));

        // 개수를 알린다 — 되돌릴 수 있다는 것까지.
        Assert.Contains("2개를 휴지통으로 보냈다", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    /// <summary>확인창을 두지 않는다 — 되돌릴 수 있는 동작이다 (§4.4).</summary>
    [Fact]
    public async Task NoConfirmationStandsInTheWay()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.Prompt.Messages);
        Assert.Single(f.RecycleBin.Sent);
    }

    [Fact]
    public async Task AFolderGoesWhole()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Recipe");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Equal(Path.Combine(f.CodePath, "Recipe"), Assert.Single(f.RecycleBin.Sent));
        Assert.False(Directory.Exists(Path.Combine(f.CodePath, "Recipe")));
    }

    [Fact]
    public async Task NothingHappensWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanTrashSelection);
        Assert.False(f.ViewModel.TrashSelectionCommand.CanExecute(null));

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.RecycleBin.Sent);
        Assert.Null(f.ViewModel.Message);
    }

    /// <summary>잠긴 파일 등으로 실패하면 요약이 상태 바에 남고 사유가 따로 보인다 (§5).</summary>
    [Fact]
    public async Task AFailureIsReportedAndTheSummaryStays()
    {
        using var f = await ReadyAsync();
        f.RecycleBin.Error = "다른 프로그램이 쓰고 있다";
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Contains("실패 1", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("다른 프로그램이 쓰고 있다", Assert.Single(f.Prompt.Reports));

        // 실패했으므로 파일은 그대로다.
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "a.dll")));
    }

    [Fact]
    public async Task TheListRefreshesAfterTrashing()
    {
        using var f = await ReadyAsync();
        var output = f.ViewModel.Panels[1];
        Assert.Equal(2, output.Items.Count);

        Select(output, "a.dll");
        await f.ViewModel.TrashSelectionAsync();

        Assert.Single(output.Items);
        Assert.DoesNotContain("a.dll", output.Items.Select(i => i.Name));
    }

    /// <summary>
    /// <c>Shift+Del</c>(영구 삭제)은 탐색기와 달리 막는다 (§4.4) — 되돌릴 수 없는 동작을
    /// 실수로 부르는 자리를 아예 두지 않는다.
    /// </summary>
    [Fact]
    public async Task PermanentDeleteIsRefusedWithAReason()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        f.ViewModel.RejectPermanentDelete();

        Assert.Contains("영구 삭제는 하지 않는다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        // 거절은 스스로 사라진다 — 화면이 이미 "안 지웠다"고 답하고 있다 (§5.1).
        Assert.True(f.ViewModel.MessageIsTransient);
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "a.dll")));
        Assert.Empty(f.RecycleBin.Sent);
    }
}
