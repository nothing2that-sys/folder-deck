using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Tests;

/// <summary>파일을 열지 않고 무엇을 열려고 했는지만 기록한다.</summary>
internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Opened { get; } = [];

    /// <summary>탐색기에 넘긴 (경로, 폴더여부).</summary>
    public List<(string Path, bool IsDirectory)> Revealed { get; } = [];

    /// <summary>셸 속성 대화상자를 띄우려 한 경로.</summary>
    public List<string> Properties { get; } = [];

    public string? Error { get; set; }

    public string? Open(string path)
    {
        Opened.Add(path);
        return Error;
    }

    public string? RevealInExplorer(string path, bool isDirectory)
    {
        Revealed.Add((path, isDirectory));
        return Error;
    }

    public string? ShowProperties(string path)
    {
        Properties.Add(path);
        return Error;
    }
}

/// <summary>테스트는 STA 스레드가 없으므로 실제 클립보드를 건드리지 않는다.</summary>
internal sealed class FakeClipboardService : IClipboardService
{
    public List<string> Texts { get; } = [];

    /// <summary><c>SetFiles</c> 로 실린 뭉치들 — 한 번도 안 실었으면 비어 있다 (A군 ③ C-1).</summary>
    public List<IReadOnlyList<OperationItem>> FileSets { get; } = [];

    /// <summary>
    /// 지금 클립보드에 있는 데이터 개체. <b>기본값은 null = 빈 클립보드</b>다 — 이 기능을 모르는
    /// 기존 테스트가 실수로 붙여넣지 않게 (선례: <c>FakeUserPrompt.NameAnswer</c> 기본 null).
    /// </summary>
    public System.Windows.IDataObject? Data { get; set; }

    public string? Error { get; set; }

    public string? Text => Texts.Count > 0 ? Texts[^1] : null;

    public string? SetText(string text)
    {
        Texts.Add(text);
        return Error;
    }

    public string? SetFiles(IReadOnlyList<OperationItem> items, bool move = false)
    {
        FileSets.Add(items);

        if (Error is not null)
        {
            return Error;
        }

        // 실물과 같은 것이 실린다 — 무엇이 실리는지는 진짜 코드가 정한다(가짜가 아니라).
        Data = ClipboardService.FileDataObject(items, move);
        return null;
    }

    public System.Windows.IDataObject? GetData() => Data;
}

/// <summary>진짜 휴지통을 건드리지 않고 지운다 — 무엇을 보냈는지만 적는다.</summary>
internal sealed class FakeRecycleBin : IRecycleBin
{
    public List<string> Sent { get; } = [];

    public string? Error { get; set; }

    public string? Send(string path)
    {
        if (Error is not null)
        {
            return Error;
        }

        Sent.Add(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return null;
    }
}

/// <summary>
/// 임시 폴더 트리 + 워크스페이스 + 메인 뷰모델. 실제 파일 시스템을 쓰되
/// <c>%APPDATA%</c> 나 사용자 폴더는 건드리지 않는다.
/// </summary>
internal sealed class MainWindowFixture : IDisposable
{
    public const string UnreachablePath = @"\\folderdeck-no-such-host\logs";

    private readonly string _root;

    /// <param name="seedFolderReferences">
    /// 타일·대상함·매크로가 <b>한 폴더를 동시에</b> 가리키는 상태로 세운다 (§4.11-2 E3 검증용).
    /// <b>기본은 끔</b> — 기존 테스트들이 이 픽스처의 개수를 그대로 단언하므로 켜면 깨진다.
    /// </param>
    /// <param name="settings">
    /// 전역 설정 (§7.2). <b>기본은 null</b> — 그러면 뷰모델이 기본값 한 벌로 서고 타일 사이
    /// 여백이 8 이라 D56 이전과 같다. 간격을 무는 시험만 자기 것을 넘긴다.
    /// </param>
    /// <param name="settingsWritable">
    /// 설정 파일이 손상돼 <b>덮어쓰지 않기로 한 실행</b>인가 (<c>App.LoadSettings</c>).
    /// </param>
    public MainWindowFixture(
        bool seedFolderReferences = false,
        AppSettings? settings = null,
        bool settingsWritable = true)
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.AppTests", Guid.NewGuid().ToString("N"));

        // 상시 폴더 2개(코드·출력) + 순환 전용 3개(작업·문서·임시) + 접근 불가 1개.
        CodePath = MakeDir("code");
        Directory.CreateDirectory(Path.Combine(CodePath, "Recipe"));
        Directory.CreateDirectory(Path.Combine(CodePath, "Views"));
        File.WriteAllText(Path.Combine(CodePath, "Main.cs"), new string('x', 300));
        File.WriteAllText(Path.Combine(CodePath, "Recipe", "Recipe.cs"), "recipe");

        // 순환 칸으로 가는 탐색용 폴더. code 와 같은 구조지만 상시가 아니다 —
        // 상시 폴더를 클릭하면 순환 칸이 아니라 자기 상시 칸이 반응하기 때문이다(§4.1).
        WorkPath = MakeDir("work");
        Directory.CreateDirectory(Path.Combine(WorkPath, "Recipe"));
        Directory.CreateDirectory(Path.Combine(WorkPath, "Views"));
        File.WriteAllText(Path.Combine(WorkPath, "Main.cs"), new string('x', 300));
        File.WriteAllText(Path.Combine(WorkPath, "Recipe", "Recipe.cs"), "recipe");

        OutputPath = MakeDir("output");
        File.WriteAllText(Path.Combine(OutputPath, "a.dll"), "a");
        File.WriteAllText(Path.Combine(OutputPath, "b.dll"), "bb");

        DocsPath = MakeDir("docs");
        File.WriteAllText(Path.Combine(DocsPath, "spec.md"), "spec");

        Workspace = new Workspace
        {
            Title = "테스트 작업",
            Folders =
            [
                new FolderEntry
                {
                    Path = CodePath, DisplayName = "code", Description = "구현 코드",
                    Pinned = true, ViewMode = FolderViewMode.Details, SortBy = SortBy.Name,
                },
                new FolderEntry
                {
                    Path = OutputPath, DisplayName = "산출물", Description = "빌드 결과",
                    Pinned = true, ViewMode = FolderViewMode.Details, SortBy = SortBy.Size, SortDesc = true,
                },
                new FolderEntry
                {
                    Path = WorkPath, DisplayName = "작업", Description = "탐색용",
                    Pinned = false, ViewMode = FolderViewMode.Details, SortBy = SortBy.Name,
                },
                new FolderEntry
                {
                    Path = DocsPath, DisplayName = "문서", Description = "사양",
                    Pinned = false, ViewMode = FolderViewMode.List, SortBy = SortBy.Name,
                },
                new FolderEntry
                {
                    Path = UnreachablePath, DisplayName = "설비 로그", Description = "네트워크",
                    Pinned = false,
                },
            ],
        };

        if (seedFolderReferences)
        {
            // "code" 는 Pinned 라 상시 타일이 유도된다 — 셋(타일·대상함·매크로)이 이 폴더 하나를
            // 동시에 가리키게 만든다. 나머지 넷은 건드리지 않는다.
            var code = Workspace.Folders[0];

            Workspace.CopyTray = [code.Id];
            Workspace.Macros =
            [
                new MacroDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "code 로 복사",
                    Op = FileOperationKind.Copy,
                    Source = new MacroSource { Kind = MacroSourceKind.Selection },
                    Dest = new MacroDest { Kind = MacroDestKind.FolderIds, FolderIds = [code.Id] },
                },
            ];
        }

        Paths = new FolderDeckPaths(Path.Combine(_root, "appdata"));
        Paths.EnsureCreated();
        Store = new WorkspaceStore(Paths, new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(20)));
        Shell = new FakeShellLauncher();
        Clipboard = new FakeClipboardService();
        RecycleBin = new FakeRecycleBin();
        Prompt = new FakeUserPrompt();
        MacroEditor = new FakeMacroEditor();
        FolderEditor = new FakeFolderEditor();
        IconSettingsEditor = new FakeIconSettingsEditor();
        Engine = new FileOperationEngine(RecycleBin);
        SelfLauncher = new FakeSelfLauncher();
        Settings = settings;

        ViewModel = new MainViewModel(
            Workspace, Store, new FolderEnumerator(), Shell, Clipboard, Engine, Prompt, MacroEditor,
            FolderEditor, SelfLauncher, settings, settingsWritable, IconSettingsEditor);
    }

    /// <summary>이 픽스처가 뷰모델에 넘긴 전역 설정. 안 넘겼으면 null 이다.</summary>
    public AppSettings? Settings { get; }

    public string CodePath { get; }

    /// <summary>순환 칸으로 보낼 수 있는 탐색용 폴더 (상시가 아니다).</summary>
    public string WorkPath { get; }

    public string OutputPath { get; }

    public string DocsPath { get; }

    public Workspace Workspace { get; }

    public FolderDeckPaths Paths { get; }

    public WorkspaceStore Store { get; }

    public FakeShellLauncher Shell { get; }

    public FakeClipboardService Clipboard { get; }

    public FakeRecycleBin RecycleBin { get; }

    public FakeUserPrompt Prompt { get; }

    public FakeMacroEditor MacroEditor { get; }

    public FakeFolderEditor FolderEditor { get; }

    public FakeIconSettingsEditor IconSettingsEditor { get; }

    public FileOperationEngine Engine { get; }

    public FakeSelfLauncher SelfLauncher { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// 순환1 칸의 패널. 마이그레이션이 순환 타일을 항상 하나 만들므로 이 픽스처에서는 언제나 있다
    /// (뷰모델 쪽은 "순환 칸을 전부 지운" 상태를 허용해 nullable 이다).
    /// </summary>
    public FolderPanelViewModel Rotating => ViewModel.RotatingPanel!;

    /// <summary>재귀 검색용으로 깊은 트리를 심는다 (code · work 양쪽에 같은 모양으로).</summary>
    public void MakeDeepTree()
    {
        foreach (var root in new[] { CodePath, WorkPath })
        {
            // work/Recipe/Controls/NumberBox.xaml, work/Views/NumberBox.cs 처럼
            // 같은 이름 조각이 여러 깊이에 흩어지게.
            var controls = Directory.CreateDirectory(Path.Combine(root, "Recipe", "Controls")).FullName;
            File.WriteAllText(Path.Combine(controls, "NumberBox.xaml"), "x");
            File.WriteAllText(Path.Combine(root, "Views", "NumberBoxHost.cs"), "x");
            File.WriteAllText(Path.Combine(root, "Recipe", "NumberFormat.cs"), "x");
            File.WriteAllText(Path.Combine(root, "Untouched.txt"), "x");
        }
    }

    public FolderRowViewModel Row(string displayName) =>
        ViewModel.Rows.Single(r => r.DisplayName == displayName);

    private string MakeDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
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
