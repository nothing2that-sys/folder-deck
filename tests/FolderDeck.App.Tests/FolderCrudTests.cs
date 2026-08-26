using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 앱을 켠 채로 폴더를 등록·제거·수정한다 (§4.11).
/// </summary>
/// <remarks>
/// <b>기존 픽스처의 고정 단언(<c>Rows.Count == 5</c> 셋)을 건드리지 않는다.</b> 여기 테스트는
/// 저마다 새 <see cref="MainWindowFixture"/> 를 쓰므로 그 다섯 개에서 출발해 늘고 준다.
/// </remarks>
public sealed class FolderCrudTests
{
    /// <summary>타일 파일 목록의 하위 폴더 항목 하나 — 등록의 진입점이 받는 인자다.</summary>
    private static FileItemViewModel FolderItemAt(string fullPath) =>
        new(new FolderItem(
            Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
            fullPath,
            IsDirectory: true,
            Size: null,
            ModifiedUtc: DateTime.UtcNow));

    // ── 1. 등록 (§4.11-1) ────────────────────────────────────────────────────

    [Fact]
    public void RegisteringAddsToBothFoldersAndRows()
    {
        using var f = new MainWindowFixture();
        var target = Path.Combine(f.CodePath, "Recipe");

        f.ViewModel.RegisterFolder(FolderItemAt(target));

        // 저장되는 folders[] 와 화면이 읽는 Rows 는 서로를 따라가지 않는다 — 둘 다 늘어야 한다.
        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Equal(6, f.ViewModel.Rows.Count);

        // 맨 뒤에 붙는다 (MainViewModelTests 가 Rows 순서를 단언한다).
        var added = f.Workspace.Folders[^1];
        Assert.Equal(target, added.Path);
        Assert.Same(added, f.ViewModel.Rows[^1].Entry);

        // displayName 은 잎 이름, description 은 빈 값으로 시작한다.
        Assert.Equal("Recipe", added.DisplayName);
        Assert.Null(added.Description);
    }

    // ── 2. 같은 path 재등록 거절 (D3) ────────────────────────────────────────

    [Fact]
    public void RegisteringAPathThatIsAlreadyRegisteredIsRejected()
    {
        using var f = new MainWindowFixture();

        // 끝에 구분자가 붙어도 같은 경로다 — 정규화 뒤에 비교한다.
        f.ViewModel.RegisterFolder(FolderItemAt(f.DocsPath + Path.DirectorySeparatorChar));

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);

        // 거절하고 이유를 보인다.
        Assert.Contains("이미 등록된 폴더다", f.ViewModel.Message!);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public void FolderListAddRegistersSelectionAndSkipsDuplicates()
    {
        using var f = new MainWindowFixture();
        var target = Path.Combine(f.CodePath, "Recipe");

        f.ViewModel.AddFolders([target, f.DocsPath]);

        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Equal(6, f.ViewModel.Rows.Count);
        Assert.Equal(target, f.Workspace.Folders[^1].Path);
        Assert.Contains("폴더 1개를 등록했다", f.ViewModel.Message!);
        Assert.Contains("이미 등록 1개 제외", f.ViewModel.Message!);
    }

    [Fact]
    public void EmptyFolderSelectionDoesNotChangeRegistration()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.AddFolders([]);

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);
    }

    /// <summary>
    /// 폴더 고르기 대화상자는 <b>창</b>이 든다 — 뷰모델로 새지 않는다 (D38).
    /// 뷰모델 생성자 열 인자를 지키는 못은 <c>ClipboardCopyPasteTests</c> 에 따로 있다.
    /// </summary>
    [Fact]
    public void TheMainWindowTakesTheFolderPicker()
    {
        var parameters = Assert.Single(typeof(MainWindow).GetConstructors()).GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(MainViewModel), parameters[0].ParameterType);
        Assert.Equal(typeof(IFolderPicker), parameters[1].ParameterType);
    }

    // ── 3. 제거 (§4.11-2) ────────────────────────────────────────────────────

    [Fact]
    public void RemovingTakesItOutOfBothFoldersAndRows()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;

        f.ViewModel.RemoveFolder(f.Row("문서"));

        Assert.Equal(4, f.Workspace.Folders.Count);
        Assert.Equal(4, f.ViewModel.Rows.Count);
        Assert.DoesNotContain(f.Workspace.Folders, e => e.DisplayName == "문서");
        Assert.DoesNotContain(f.ViewModel.Rows, r => r.DisplayName == "문서");
    }

    // ── 4. 고아 참조 셋을 같은 규칙으로 (E3) ─────────────────────────────────

    [Fact]
    public void RemovingCleansTilesTrayAndMacrosAndSaysHowMany()
    {
        using var f = new MainWindowFixture(seedFolderReferences: true);
        var row = f.Row("code");
        var id = row.Entry.Id;

        // 셋이 다 이 폴더를 가리키고 있다.
        Assert.Contains(f.Workspace.Tiles!, t => t.Kind == TileKind.Pinned && t.FolderId == id);
        Assert.Single(f.ViewModel.Tray);
        Assert.Equal(id, Assert.Single(f.Workspace.Macros![0].Dest!.FolderIds!));

        f.Prompt.Answer = true;
        f.ViewModel.RemoveFolder(row);

        // 확인창이 셋을 숫자로 보였다 — 어림수가 아니라 실제로 센 값이다.
        Assert.Contains("타일 1개 · 대상함 1개 · 매크로 1개", Assert.Single(f.Prompt.Messages));

        Assert.DoesNotContain(f.Workspace.Tiles!, t => t.FolderId == id);
        Assert.Empty(f.ViewModel.Tray);
        Assert.Null(f.Workspace.CopyTray);

        // 매크로 정의에 남은 죽은 id 도 이때 같이 지운다 — 실행할 때만 거르지 않는다.
        Assert.Null(f.Workspace.Macros![0].Dest!.FolderIds);
    }

    // ── 5. 취소는 아무것도 바꾸지 않는다 ─────────────────────────────────────

    [Fact]
    public void CancellingRemovalChangesNothing()
    {
        using var f = new MainWindowFixture(seedFolderReferences: true);
        var row = f.Row("code");
        var id = row.Entry.Id;
        var tileCount = f.Workspace.Tiles!.Count;

        f.Prompt.Answer = false;
        f.ViewModel.RemoveFolder(row);

        // 묻기는 했다 — 세는 것이 상태를 바꾸지 않았을 뿐이다.
        Assert.Single(f.Prompt.Messages);

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(tileCount, f.Workspace.Tiles!.Count);
        Assert.Contains(f.Workspace.Tiles!, t => t.Kind == TileKind.Pinned && t.FolderId == id);
        Assert.Equal(id, Assert.Single(f.Workspace.CopyTray!));
        Assert.Equal(id, Assert.Single(f.Workspace.Macros![0].Dest!.FolderIds!));
        Assert.Single(f.ViewModel.Tray);
    }

    // ── 6. 수정 (§4.11-3 · D2) ───────────────────────────────────────────────

    [Fact]
    public void EditingChangesNameAndDescriptionAndLeavesPinnedAlone()
    {
        using var f = new MainWindowFixture();
        var row = f.Row("code");

        // 상시 폴더로 고른다 — pinned 이 true 인 채로 남아야 D2 가 확인된다.
        Assert.True(row.Entry.Pinned);
        var viewMode = row.Entry.ViewMode;
        var sortBy = row.Entry.SortBy;
        var rotationSlot = row.Entry.RotationSlot;

        f.FolderEditor.Answer = new FolderEditDraft("코드 저장소", "설명이 처음 들어간다");
        f.ViewModel.EditFolder(row);

        // 창에는 옛 값이 미리 채워져 떴다.
        var opened = Assert.Single(f.FolderEditor.Opened);
        Assert.Equal("code", opened.DisplayName);
        Assert.Equal(f.CodePath, f.FolderEditor.OpenedPath);

        Assert.Equal("코드 저장소", row.Entry.DisplayName);
        Assert.Equal("설명이 처음 들어간다", row.Entry.Description);

        // 화면이 읽는 쪽도 같이 갱신된다 (Entry 에서 계산되는 값이라 알림을 손으로 건다).
        Assert.Equal("코드 저장소", row.DisplayName);
        Assert.True(row.HasDescription);

        // 고치는 것은 둘뿐이다.
        Assert.True(row.Entry.Pinned);
        Assert.Equal(viewMode, row.Entry.ViewMode);
        Assert.Equal(sortBy, row.Entry.SortBy);
        Assert.Equal(rotationSlot, row.Entry.RotationSlot);
    }

    // ── 8. 스키마는 그대로다 ─────────────────────────────────────────────────

    [Fact]
    public void SchemaVersionStaysWhereItWasThroughAllThree()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        f.FolderEditor.Answer = new FolderEditDraft("문서들", "사양 모음");

        f.ViewModel.RegisterFolder(FolderItemAt(Path.Combine(f.CodePath, "Views")));
        f.ViewModel.EditFolder(f.Row("문서"));
        f.ViewModel.RemoveFolder(f.Row("작업"));
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        Assert.Equal(Workspace.CurrentSchemaVersion, reopened.SchemaVersion);

        // 셋 다 파일까지 갔다 (5 - 1 + 1).
        Assert.Equal(5, reopened.Folders.Count);
        Assert.Contains(reopened.Folders, e => e.DisplayName == "Views");
        Assert.Contains(reopened.Folders, e => e.Description == "사양 모음");
        Assert.DoesNotContain(reopened.Folders, e => e.DisplayName == "작업");
    }
}
