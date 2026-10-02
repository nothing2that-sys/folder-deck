using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class LauncherTests
{
    [Fact]
    public void EmptyStoreShowsAnEmptyLauncherInsteadOfFailing()
    {
        using var f = new LauncherFixture();

        Assert.Empty(f.ViewModel.Cards);
        Assert.True(f.ViewModel.HasNoWorkspaces);
    }

    [Fact]
    public void ListsSeededWorkspacesWithTitleFolderCountAndLastUsed()
    {
        using var f = new LauncherFixture();
        f.Seed("Sample Item 작업", folderCount: 6);
        f.ViewModel.Refresh();

        var card = Assert.Single(f.ViewModel.Cards);
        Assert.Equal("Sample Item 작업", card.Title);
        Assert.Contains("폴더 6", card.MetaText);
        Assert.Equal("Sample Item 작업 설명", card.Description);
        Assert.Contains("마지막 사용", card.MetaText);
        Assert.False(f.ViewModel.HasNoWorkspaces);
    }

    [Fact]
    public void ListComesFromEnumeratingTheWorkspacesFolder()
    {
        using var f = new LauncherFixture();
        f.Seed("첫째");
        f.ViewModel.Refresh();
        Assert.Single(f.ViewModel.Cards);

        f.Seed("둘째");
        f.ViewModel.Refresh();

        Assert.Equal(2, f.ViewModel.Cards.Count);
    }

    [Fact]
    public void SortsByLastUsedNewestFirst()
    {
        using var f = new LauncherFixture();
        f.Seed("오래된 것", lastUsed: DateTimeOffset.Now.AddDays(-10));
        f.Seed("최근 것", lastUsed: DateTimeOffset.Now);
        f.Seed("중간 것", lastUsed: DateTimeOffset.Now.AddDays(-2));
        f.ViewModel.Refresh();

        Assert.Equal(["최근 것", "중간 것", "오래된 것"], f.ViewModel.Cards.Select(c => c.Title));
    }

    [Fact]
    public void MetaSaysNoUsageHistoryWhenNeverOpened()
    {
        using var f = new LauncherFixture();
        f.Seed("안 열어본 것", lastUsed: DateTimeOffset.MinValue);
        f.ViewModel.Refresh();

        Assert.Contains("사용 기록 없음", Assert.Single(f.ViewModel.Cards).MetaText);
    }

    [Fact]
    public void CorruptFileIsShownAsCorruptWhileTheRestStayUsable()
    {
        using var f = new LauncherFixture();
        f.Seed("정상 하나");
        f.Seed("정상 둘");
        var corruptPath = f.SeedCorrupt();
        f.ViewModel.Refresh();

        Assert.Equal(3, f.ViewModel.Cards.Count);

        var corrupt = Assert.Single(f.ViewModel.Cards, c => c.IsCorrupt);
        Assert.Equal(Path.GetFileName(corruptPath), corrupt.Title);
        Assert.Equal("이 작업 관리 파일을 읽지 못했습니다. 다른 항목은 정상입니다.", corrupt.CorruptText);
        Assert.NotNull(corrupt.CorruptDetail);
        Assert.Equal(corruptPath, corrupt.PathText);
        Assert.False(corrupt.IsUsable);

        Assert.Equal(2, f.ViewModel.Cards.Count(c => c.IsUsable));
    }

    [Fact]
    public void CorruptEntriesGoLastAndCannotBeOpened()
    {
        using var f = new LauncherFixture();
        f.SeedCorrupt();
        f.Seed("정상");
        f.ViewModel.Refresh();

        Assert.True(f.ViewModel.Cards[^1].IsCorrupt);

        f.ViewModel.Open(f.ViewModel.Cards[^1]);
        Assert.Empty(f.Opened);
    }

    [Fact]
    public void OpenStartsANewProcessForTheWorkspace()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("열 것");
        f.ViewModel.Refresh();

        f.ViewModel.Open(f.ViewModel.Cards[0]);

        Assert.Equal(seeded.Id, Assert.Single(f.Opened));
    }

    [Fact]
    public void EditingTitleAndDescriptionPersists()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("원래 제목");
        f.ViewModel.Refresh();

        var card = f.ViewModel.Cards[0];
        card.Title = "고친 제목";
        card.Description = "고친 설명";
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(seeded.Id).ValueOrThrow();
        Assert.Equal("고친 제목", reloaded.Title);
        Assert.Equal("고친 설명", reloaded.Description);
    }

    [Fact]
    public void BlankDescriptionIsStoredAsNullSoItIsOmitted()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("설명 지움");
        f.ViewModel.Refresh();

        f.ViewModel.Cards[0].Description = "   ";
        f.Store.Flush();

        Assert.Null(f.Store.LoadWorkspace(seeded.Id).ValueOrThrow().Description);
    }

    [Fact]
    public void CannotCreateWithoutATitleAndAtLeastOneFolder()
    {
        using var f = new LauncherFixture();
        f.ViewModel.StartCreate();

        Assert.False(f.ViewModel.CanCreate);

        f.ViewModel.NewTitle = "제목만";
        Assert.False(f.ViewModel.CanCreate);

        f.ViewModel.AddFolders([f.MakeRealFolder("코드")]);
        Assert.True(f.ViewModel.CanCreate);

        f.ViewModel.NewTitle = "   ";
        Assert.False(f.ViewModel.CanCreate);
    }

    [Fact]
    public void CreatingAWorkspaceWritesItAndShowsItInTheList()
    {
        using var f = new LauncherFixture();
        var code = f.MakeRealFolder("code");
        var logs = f.MakeRealFolder("logs");

        f.ViewModel.StartCreate();
        f.ViewModel.NewTitle = "새 프로젝트";
        f.ViewModel.AddFolders([code, logs]);
        f.ViewModel.Create();

        var card = Assert.Single(f.ViewModel.Cards);
        Assert.Equal("새 프로젝트", card.Title);
        Assert.Contains("폴더 2", card.MetaText);
        Assert.False(f.ViewModel.IsCreating);

        var onDisk = f.Store.LoadWorkspace(card.Workspace!.Id).ValueOrThrow();
        Assert.Equal(["code", "logs"], onDisk.Folders.Select(x => x.DisplayName));
        Assert.Equal(Workspace.CurrentSchemaVersion, onDisk.SchemaVersion);
        Assert.True(File.Exists(f.Paths.WorkspaceFile(card.Workspace.Id)));
    }

    [Fact]
    public void FirstFolderIsPinnedByDefault()
    {
        using var f = new LauncherFixture();
        f.ViewModel.StartCreate();
        f.ViewModel.NewTitle = "핀 확인";
        f.ViewModel.AddFolders([f.MakeRealFolder("a"), f.MakeRealFolder("b"), f.MakeRealFolder("c")]);
        f.ViewModel.Create();

        var folders = f.ViewModel.Cards[0].Workspace!.Folders;
        Assert.True(folders[0].Pinned);
        Assert.False(folders[1].Pinned);
        Assert.False(folders[2].Pinned);
    }

    [Fact]
    public void DuplicateFoldersAreNotAddedTwice()
    {
        using var f = new LauncherFixture();
        var code = f.MakeRealFolder("code");
        f.ViewModel.StartCreate();

        f.ViewModel.AddFolders([code, code, code + Path.DirectorySeparatorChar]);

        Assert.Single(f.ViewModel.NewFolders);
    }

    [Fact]
    public void FolderPickerFeedsTheSameList()
    {
        using var f = new LauncherFixture();
        f.Picker.NextResult.Add(f.MakeRealFolder("picked"));
        f.ViewModel.StartCreate();

        f.ViewModel.AddFoldersFromPicker();

        Assert.Equal(1, f.Picker.CallCount);
        Assert.Single(f.ViewModel.NewFolders);
    }

    [Fact]
    public void FoldersCanBeRemovedFromTheDraft()
    {
        using var f = new LauncherFixture();
        var a = f.MakeRealFolder("a");
        f.ViewModel.StartCreate();
        f.ViewModel.AddFolders([a, f.MakeRealFolder("b")]);

        f.ViewModel.RemoveNewFolder(a);

        Assert.Single(f.ViewModel.NewFolders);
        Assert.DoesNotContain(a, f.ViewModel.NewFolders);
    }

    [Fact]
    public void CancellingCreateDropsTheDraft()
    {
        using var f = new LauncherFixture();
        f.ViewModel.StartCreate();
        f.ViewModel.NewTitle = "버릴 것";
        f.ViewModel.AddFolders([f.MakeRealFolder("x")]);

        f.ViewModel.CancelCreate();

        Assert.False(f.ViewModel.IsCreating);
        Assert.Empty(f.ViewModel.NewFolders);
        Assert.Equal(string.Empty, f.ViewModel.NewTitle);
        Assert.Empty(f.ViewModel.Cards);
    }

    [Fact]
    public void DeleteRemovesOnlyTheJsonAndLeavesRealFoldersAlone()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("지울 것", folderCount: 3);
        var realPaths = seeded.Folders.Select(x => x.Path).ToList();
        Assert.All(realPaths, p => Assert.True(Directory.Exists(p)));
        f.ViewModel.Refresh();

        f.ViewModel.Delete(f.ViewModel.Cards[0]);

        Assert.False(File.Exists(f.Paths.WorkspaceFile(seeded.Id)));
        Assert.Empty(f.ViewModel.Cards);

        Assert.All(realPaths, p => Assert.True(Directory.Exists(p), $"실제 폴더가 지워졌다: {p}"));
    }

    [Fact]
    public void DeleteConfirmationSpellsOutThatRealFoldersSurvive()
    {
        using var f = new LauncherFixture();
        f.Seed("문구 확인");
        f.ViewModel.Refresh();

        f.ViewModel.Delete(f.ViewModel.Cards[0]);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("실제 폴더는 지워지지 않습니다", message);
        Assert.Contains(LauncherViewModel.DeleteNotice, message);
    }

    [Fact]
    public void DecliningTheConfirmationKeepsTheWorkspace()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("남길 것");
        f.ViewModel.Refresh();
        f.Prompt.Answer = false;

        f.ViewModel.Delete(f.ViewModel.Cards[0]);

        Assert.True(File.Exists(f.Paths.WorkspaceFile(seeded.Id)));
        Assert.Single(f.ViewModel.Cards);
    }

    [Fact]
    public void DeletingTheAutoOpenTargetClearsIt()
    {
        using var f = new LauncherFixture();
        var seeded = f.Seed("자동 열기 대상");
        f.Settings.LastWorkspaceId = seeded.Id;
        f.ViewModel.Refresh();

        f.ViewModel.Delete(f.ViewModel.Cards[0]);

        Assert.Null(f.Settings.LastWorkspaceId);
    }

    [Fact]
    public void SkipLauncherTogglePersistsBothWays()
    {
        using var f = new LauncherFixture();
        Assert.False(f.ViewModel.SkipLauncher);

        f.ViewModel.SkipLauncher = true;
        f.Store.Flush();
        Assert.True(f.Store.LoadSettings().ValueOrThrow().SkipLauncher);

        f.ViewModel.SkipLauncher = false;
        f.Store.Flush();
        Assert.False(f.Store.LoadSettings().ValueOrThrow().SkipLauncher);
    }

    [Fact]
    public void ToggleIsLockedWhenSettingsAreNotWritable()
    {
        using var f = new LauncherFixture(settingsWritable: false);

        Assert.False(f.ViewModel.CanChangeSkipLauncher);

        f.ViewModel.SkipLauncher = true;
        f.Store.Flush();

        Assert.False(File.Exists(f.Paths.SettingsFile));
    }
}
