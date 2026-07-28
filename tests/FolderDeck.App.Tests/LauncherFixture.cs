using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Tests;

internal sealed class FakeFolderPicker : IFolderPicker
{
    public List<string> NextResult { get; } = [];

    public int CallCount { get; private set; }

    public IReadOnlyList<string> PickFolders(string title)
    {
        CallCount++;
        return NextResult;
    }
}

internal sealed class FakeUserPrompt : IUserPrompt
{
    public bool Answer { get; set; } = true;

    public List<string> Messages { get; } = [];

    /// <summary>Report 로 올라온 것만 따로 — 부분 실패 내역 검증용.</summary>
    public List<string> Reports { get; } = [];

    public bool Confirm(string title, string message)
    {
        Messages.Add(message);
        return Answer;
    }

    public void Report(string title, string message) => Reports.Add(message);

    // ── 동일 이름 충돌 (§4.3) ────────────────────────────────────────────────

    /// <summary>충돌 창이 뜬 횟수와 본문. 안 겹치면 비어 있어야 한다.</summary>
    public List<string> ConflictMessages { get; } = [];

    /// <summary>
    /// 창이 돌려줄 답. 기본은 "덮어쓰기 · 기억 안 함"이라 Stage 3 까지의 동작이 그대로다.
    /// null 로 두면 사용자가 취소한 것.
    /// </summary>
    public ConflictDecision? ConflictAnswer { get; set; } =
        new(ConflictPolicy.Overwrite, Remember: false);

    /// <summary>창을 띄울 때 넘어온 "지금 정책" — 기본 단추가 무엇이었는지 확인용.</summary>
    public ConflictPolicy? SuggestedPolicy { get; private set; }

    public ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested)
    {
        ConflictMessages.Add(message);
        SuggestedPolicy = suggested;
        return ConflictAnswer;
    }
}

/// <summary>매크로 편집 창 대신 답만 돌려준다 (§4.7).</summary>
internal sealed class FakeMacroEditor : IMacroEditor
{
    /// <summary>창이 돌려줄 값. null 이면 사용자가 취소한 것.</summary>
    public MacroDraft? Answer { get; set; }

    /// <summary>미리 채워져 들어온 값 — 지금 화면 상태가 제대로 실렸는지 확인용.</summary>
    public MacroDraft? Suggested { get; private set; }

    public bool? CanFixPath { get; private set; }

    public int CallCount { get; private set; }

    /// <summary>들어온 제안을 그대로 받아들이게 한다(이름만 바꾸는 흔한 경우).</summary>
    public Func<MacroDraft, MacroDraft>? Transform { get; set; }

    public MacroDraft? Edit(MacroDraft draft, bool canFixPath)
    {
        CallCount++;
        Suggested = draft;
        CanFixPath = canFixPath;

        return Transform is not null ? Transform(draft) : Answer;
    }
}

/// <summary>런처 뷰모델 + 격리된 <c>%APPDATA%</c> 대체 폴더.</summary>
internal sealed class LauncherFixture : IDisposable
{
    private readonly string _root;

    public LauncherFixture(bool settingsWritable = true)
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.LauncherTests", Guid.NewGuid().ToString("N"));
        Paths = new FolderDeckPaths(Path.Combine(_root, "appdata"));
        Paths.EnsureCreated();
        Store = new WorkspaceStore(Paths, new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(10)));
        Settings = new AppSettings();
        Picker = new FakeFolderPicker();
        Prompt = new FakeUserPrompt();
        Shell = new FakeShellLauncher();
        ViewModel = new LauncherViewModel(
            Store, Settings, Opened.Add, Picker, Prompt, Shell, settingsWritable);
    }

    public FolderDeckPaths Paths { get; }

    public WorkspaceStore Store { get; }

    public AppSettings Settings { get; }

    public FakeFolderPicker Picker { get; }

    public FakeUserPrompt Prompt { get; }

    public FakeShellLauncher Shell { get; }

    public List<Workspace> Opened { get; } = [];

    public LauncherViewModel ViewModel { get; }

    /// <summary>실제로 존재하는 폴더를 만들어 준다(드롭·선택 대상용).</summary>
    public string MakeRealFolder(string name)
    {
        var path = Path.Combine(_root, "real", name);
        Directory.CreateDirectory(path);
        return path;
    }

    public Workspace Seed(string title, int folderCount = 2, DateTimeOffset? lastUsed = null)
    {
        var workspace = new Workspace
        {
            Title = title,
            Description = $"{title} 설명",
            LastUsed = lastUsed ?? DateTimeOffset.Now,
            Folders = [.. Enumerable.Range(0, folderCount).Select(i => new FolderEntry
            {
                Path = MakeRealFolder($"{title}-{i}"),
                DisplayName = $"folder{i}",
            })],
        };

        Assert.Null(Store.SaveWorkspace(workspace));
        return workspace;
    }

    /// <summary>읽을 수 없는 워크스페이스 파일을 섞는다.</summary>
    public string SeedCorrupt()
    {
        var path = Paths.WorkspaceFile(Guid.NewGuid());
        File.WriteAllText(path, "{ 이건 JSON 이 아니다 ");
        return path;
    }

    public void Dispose()
    {
        Store.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 무시.
        }
    }
}
