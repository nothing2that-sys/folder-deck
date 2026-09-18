using FolderDeck.Core.Renaming;

namespace FolderDeck.App.Tests;

/// <summary>
/// 배치 리네임(기능 후보 ③, 2026-09-18 설계, doc/FeatureCandidates_2026-09-18_design.md
/// §③) — Ctrl+F2(<c>FolderPanelViewModel.BatchRenameSelectionCommand</c>)부터 실제
/// <c>Move</c>·새로고침까지.
/// </summary>
public sealed class BatchRenameTests
{
    [Fact]
    public async Task NothingSelectedNeverAsksAndNeverOpensADialog()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [];

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.BatchRenameAsked);
    }

    [Fact]
    public async Task CancellingThePlanLeavesDiskUntouched()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items.Single(i => i.Name == "Main.cs")];
        f.Prompt.BatchRenameAnswer = null; // 취소

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.Single(f.Prompt.BatchRenameAsked);
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }

    [Fact]
    public async Task AskedNamesExcludeOnlyTheSelectedOnesFromOtherExisting()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items.Single(i => i.Name == "Main.cs")];
        f.Prompt.BatchRenameAnswer = null;

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        var (originalNames, otherNames) = f.Prompt.BatchRenameAsked[0];
        Assert.Equal(["Main.cs"], originalNames);
        Assert.DoesNotContain("Main.cs", otherNames);
        Assert.Contains("Recipe", otherNames);
        Assert.Contains("Views", otherNames);
    }

    [Fact]
    public async Task ConfirmedPlanRenamesOnDiskAndRefreshesThePanel()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "photo1.jpg"), "a");
        File.WriteAllText(Path.Combine(f.CodePath, "photo2.jpg"), "b");
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var selected = new[]
        {
            code.Items.Single(i => i.Name == "photo1.jpg"),
            code.Items.Single(i => i.Name == "photo2.jpg"),
        };
        code.SelectedItems = selected;

        f.Prompt.BatchRenameAnswer =
        [
            new RenamePreviewRow("photo1.jpg", "vacation-01.jpg", HasNameConflict: false, InvalidReason: null),
            new RenamePreviewRow("photo2.jpg", "vacation-02.jpg", HasNameConflict: false, InvalidReason: null),
        ];

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(f.CodePath, "photo1.jpg")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "photo2.jpg")));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "vacation-01.jpg")));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "vacation-02.jpg")));

        // 새로고침까지 됐는지 — 목록에 새 이름이 보여야 한다.
        Assert.Contains(code.Items, i => i.Name == "vacation-01.jpg");
        Assert.Contains(code.Items, i => i.Name == "vacation-02.jpg");
    }

    [Fact]
    public async Task UnchangedRowsAreSkippedRatherThanTouched()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var mainCs = code.Items.Single(i => i.Name == "Main.cs");
        code.SelectedItems = [mainCs];

        var beforeWriteTime = File.GetLastWriteTimeUtc(Path.Combine(f.CodePath, "Main.cs"));

        f.Prompt.BatchRenameAnswer =
        [
            new RenamePreviewRow("Main.cs", "Main.cs", HasNameConflict: false, InvalidReason: null),
        ];

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(Path.Combine(f.CodePath, "Main.cs")));
    }

    [Fact]
    public async Task PartialFailureReportsWhichOnesFailedAndStillRenamesTheRest()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "a.txt"), "a");
        File.WriteAllText(Path.Combine(f.CodePath, "b.txt"), "b");
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var selected = new[]
        {
            code.Items.Single(i => i.Name == "a.txt"),
            code.Items.Single(i => i.Name == "b.txt"),
        };
        code.SelectedItems = selected;

        // b.txt 는 이미 디스크에 있는 이름(Main.cs)으로 보내 실행 시점 실패를 흉내낸다 —
        // 미리보기 계산 뒤 디스크가 바뀌는 경우를 시늉하는 것이지, 정상 경로에서 이런
        // RenamePreviewRow 가 나오지는 않는다(그랬다면 HasNameConflict 가 이미 true).
        f.Prompt.BatchRenameAnswer =
        [
            new RenamePreviewRow("a.txt", "renamed-a.txt", HasNameConflict: false, InvalidReason: null),
            new RenamePreviewRow("b.txt", "Main.cs", HasNameConflict: false, InvalidReason: null),
        ];

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(f.CodePath, "renamed-a.txt")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "renamed-b.txt")));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "b.txt"))); // 실패해서 그대로 남았다
        Assert.Single(f.Prompt.Reports);
        Assert.Contains("1개", f.Prompt.Reports[0]);
    }
}
