using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class MacroTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    private static void Accept(MainWindowFixture f, Func<MacroDraft, MacroDraft>? tweak = null) =>
        f.MacroEditor.Transform = d => tweak is null ? d : tweak(d);

    [Fact]
    public async Task NoMacroWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.True(f.ViewModel.MacrosAreEmpty);
        Assert.False(f.ViewModel.CanCreateMacro);

        f.ViewModel.CreateMacro();

        Assert.Equal(0, f.MacroEditor.CallCount);
        Assert.True(f.ViewModel.MacrosAreEmpty);
    }

    [Fact]
    public async Task TheEditorOpensPrefilledFromTheCurrentState()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Accept(f);

        f.ViewModel.CreateMacro();

        var suggested = f.MacroEditor.Suggested!;
        Assert.Equal(FileOperationKind.Copy, suggested.Op);
        Assert.Equal(MacroSourceKind.Selection, suggested.SourceKind);
        Assert.Equal(MacroDestKind.Tray, suggested.DestKind);
        Assert.True(suggested.Confirm);
        Assert.Contains("문서", suggested.Name);

        Assert.True(f.MacroEditor.CanFixPath);
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), suggested.FixedPath);
    }

    [Fact]
    public async Task ManySourcesCannotBePinnedToOnePath()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs", "Recipe");
        Accept(f);

        f.ViewModel.CreateMacro();

        Assert.False(f.MacroEditor.CanFixPath);
        Assert.Null(f.MacroEditor.Suggested!.FixedPath);
    }

    [Fact]
    public async Task CancelingTheEditorCreatesNothing()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Answer = null;

        f.ViewModel.CreateMacro();

        Assert.True(f.ViewModel.MacrosAreEmpty);
        Assert.Null(f.Workspace.Macros);
    }

    [Fact]
    public async Task AMacroPersistsAndComesBackAfterARestart()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Accept(f, d => d with { Name = "문서로 복사", OnConflict = ConflictPolicy.Skip });

        f.ViewModel.CreateMacro();

        var card = Assert.Single(f.ViewModel.Macros);
        Assert.Equal("문서로 복사", card.Name);
        Assert.Contains("대상함", card.Summary);
        Assert.Contains("건너뛰기", card.Summary);

        f.Store.Flush();
        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt,
            f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        var reloaded = Assert.Single(restarted.Macros);
        Assert.Equal("문서로 복사", reloaded.Name);
        Assert.Equal(ConflictPolicy.Skip, reloaded.Definition.OnConflict);
        Assert.Equal(MacroSourceKind.Selection, reloaded.Definition.Source!.Kind);
    }

    [Fact]
    public async Task ConfirmCannotBeTurnedOffForMoveOrTrash()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        Accept(f, d => d with { Op = FileOperationKind.Move, Confirm = false });

        f.ViewModel.CreateMacro();

        Assert.True(f.ViewModel.Macros[0].Definition.Confirm);
    }

    [Fact]
    public async Task RemovingAMacroClearsTheSlotWhenItWasTheLast()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        Accept(f);
        f.ViewModel.CreateMacro();

        f.ViewModel.RemoveMacro(f.ViewModel.Macros[0]);

        Assert.True(f.ViewModel.MacrosAreEmpty);

        f.Store.Flush();
        Assert.Null(f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().Macros);
    }

    [Fact]
    public async Task EditDoesNotRequireASelection()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.Panels[0].ClearSelection();

        Assert.True(f.ViewModel.CanEditMacro);

        Accept(f, d => d with { Name = "새 이름" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal("새 이름", f.ViewModel.Macros[0].Name);
    }

    [Fact]
    public async Task EditingUpdatesTheExistingRuleInPlace()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        var original = f.ViewModel.Macros[0];

        Accept(f, d => d with { Name = "고친 이름", OnConflict = ConflictPolicy.Skip });
        f.ViewModel.EditMacro(original);

        var card = Assert.Single(f.ViewModel.Macros);
        Assert.Same(original, card);
        Assert.Equal("고친 이름", card.Name);
        Assert.Equal(ConflictPolicy.Skip, card.Definition.OnConflict);
    }

    [Fact]
    public async Task CancelingAnEditLeavesTheRuleUnchanged()
    {
        using var f = await WithMacroAsync(d => d with { Name = "원래 이름" }, "Main.cs");
        f.MacroEditor.Answer = null;

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal("원래 이름", f.ViewModel.Macros[0].Name);
    }

    [Fact]
    public async Task EditingPassesTheExistingValuesAsTheSuggestedDraft()
    {
        using var f = await WithMacroAsync(
            d => d with { Name = "문서로 복사", OnConflict = ConflictPolicy.Skip }, "Main.cs");
        Accept(f);

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        var suggested = f.MacroEditor.Suggested!;
        Assert.Equal("문서로 복사", suggested.Name);
        Assert.Equal(ConflictPolicy.Skip, suggested.OnConflict);
        Assert.True(f.MacroEditor.WasEdit);
    }

    [Fact]
    public async Task EditingWithoutTouchingDestKeepsThePinnedFolders()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        Accept(f, d => d with { Name = "이름만 고침" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task SwitchingToPinnedFoldersDuringEditFreezesWhatIsCheckedNow()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        Accept(f, d => d with { DestKind = MacroDestKind.FolderIds });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task EditingAFixedPathRuleWorksWithNothingCurrentlySelected()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        f.ViewModel.Panels[0].ClearSelection();

        Accept(f, d => d with { Name = "고정 규칙 고침" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.True(f.MacroEditor.CanFixPath);
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), f.MacroEditor.Suggested!.FixedPath);
        Assert.Equal("고정 규칙 고침", f.ViewModel.Macros[0].Name);
    }

    [Fact]
    public async Task RetargetingAFixedPathRuleDuringEditPointsItAtTheNewSelection()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");

        Select(f.ViewModel.Panels[0], "Recipe");
        Accept(f, d => d with { RetargetFixedPath = true });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal(
            Path.Combine(f.CodePath, "Recipe"),
            f.ViewModel.Macros[0].Definition.Source!.Path);
    }

    [Fact]
    public async Task NotRetargetingAFixedPathRuleKeepsTheOldPathEvenWithADifferentSelection()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        var originalPath = f.ViewModel.Macros[0].Definition.Source!.Path;

        Select(f.ViewModel.Panels[0], "Recipe");
        Accept(f);

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal(originalPath, f.ViewModel.Macros[0].Definition.Source!.Path);
    }

    [Fact]
    public async Task RepinningFolderIdsRuleDuringEditUpdatesTheDestinations()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        Accept(f, d => d with { RepinFolderIds = true });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    private static async Task<MainWindowFixture> WithMacroAsync(
        Func<MacroDraft, MacroDraft> tweak, params string[] selection)
    {
        var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], selection);
        f.MacroEditor.Transform = d => tweak(d);
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;
        return f;
    }

    [Fact]
    public async Task SelectionSourceUsesWhateverIsSelectedWhenItRuns()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        Select(f.ViewModel.Panels[1], "a.dll");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "a.dll")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task SelectionSourceRefusesWhenNothingIsSelected()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.Panels[0].ClearSelection();

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("대상이 없다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task AFixedPathGoesToEveryVisibleFolder()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath, DestKind = MacroDestKind.AllVisible },
            "Main.cs");

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.ViewModel.Panels[0].ClearSelection();

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    [Fact]
    public async Task AFixedPathThatVanishedSaysSoInsteadOfRunning()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        File.Delete(Path.Combine(f.CodePath, "Main.cs"));

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("고정 경로가 사라졌다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task PinnedFolderIdsIgnoreLaterTrayChanges()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task TrayDestinationFollowsTheChecksAtRunTime()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task NoDestinationSaysSoInsteadOfRunning()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("목적지가 없다", f.ViewModel.Message);
    }

    [Fact]
    public async Task AMoveMacroRefusesManyDestinations()
    {
        using var f = await WithMacroAsync(d => d with { Op = FileOperationKind.Move }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.AddToTray(f.Row("산출물"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("목적지가 2개", f.ViewModel.Message);
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }

    [Fact]
    public async Task ThePreviewSpellsOutWhatWillHappen()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = false;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        var shown = Assert.Single(f.Prompt.Messages);
        Assert.Contains("1개 → 폴더 1개 = 1건", shown);
        Assert.Contains(f.DocsPath, shown);

        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("취소", f.ViewModel.Message);
    }

    [Fact]
    public async Task ThePreviewCountsCollisionsAndSaysWhatWillBeDoneWithThem()
    {
        using var f = await WithMacroAsync(
            d => d with { OnConflict = ConflictPolicy.Skip }, "Main.cs");
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = false;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("같은 이름 1건은 건너뜁니다", Assert.Single(f.Prompt.Messages));
    }

    [Fact]
    public async Task AConfirmFreeCopyRunsWithoutAnyDialog()
    {
        using var f = await WithMacroAsync(d => d with { Confirm = false }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(f.ViewModel.Macros[0].RunsWithoutConfirm);
    }

    [Fact]
    public async Task AMacroNeverOpensTheConflictDialog()
    {
        using var f = await WithMacroAsync(
            d => d with { Confirm = false, OnConflict = ConflictPolicy.Rename }, "Main.cs");
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main (2).cs")));
    }

    [Fact]
    public async Task ATrashMacroNeedsNoDestinationAndAlwaysConfirms()
    {
        using var f = await WithMacroAsync(
            d => d with { Op = FileOperationKind.Trash, Confirm = false }, "Main.cs");
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = true;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("휴지통으로 보냅니다", Assert.Single(f.Prompt.Messages));
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), Assert.Single(f.RecycleBin.Sent));
        Assert.Contains("휴지통", f.ViewModel.Macros[0].Summary);
    }

    [Fact]
    public async Task ListsRefreshAfterAMacroRuns()
    {
        using var f = await WithMacroAsync(d => d with { Confirm = false }, "Main.cs");
        var docsTile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(docsTile.Panel.Items);

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Equal(2, docsTile.Panel.Items.Count);
    }
}
