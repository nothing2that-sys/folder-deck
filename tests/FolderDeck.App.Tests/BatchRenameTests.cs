using FolderDeck.Core.Renaming;

namespace FolderDeck.App.Tests;

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
        f.Prompt.BatchRenameAnswer = null;

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

        f.Prompt.BatchRenameAnswer =
        [
            new RenamePreviewRow("a.txt", "renamed-a.txt", HasNameConflict: false, InvalidReason: null),
            new RenamePreviewRow("b.txt", "Main.cs", HasNameConflict: false, InvalidReason: null),
        ];

        await code.BatchRenameSelectionCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(f.CodePath, "renamed-a.txt")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "renamed-b.txt")));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "b.txt")));
        Assert.Single(f.Prompt.Reports);
        Assert.Contains("1개", f.Prompt.Reports[0]);
    }
}
