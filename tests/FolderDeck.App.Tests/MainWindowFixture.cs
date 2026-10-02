using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Tests;


internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Opened { get; } = [];


    public List<(string Path, bool IsDirectory)> Revealed { get; } = [];


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


internal sealed class FakeClipboardService : IClipboardService
{
    public List<string> Texts { get; } = [];


    public List<IReadOnlyList<OperationItem>> FileSets { get; } = [];





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


        Data = ClipboardService.FileDataObject(items, move);
        return null;
    }

    public System.Windows.IDataObject? GetData() => Data;
}


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





internal sealed class MainWindowFixture : IDisposable
{
    public const string UnreachablePath = @"\\folderdeck-no-such-host\logs";

    private readonly string _root;












    public MainWindowFixture(
        bool seedFolderReferences = false,
        AppSettings? settings = null,
        bool settingsWritable = true)
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.AppTests", Guid.NewGuid().ToString("N"));


        CodePath = MakeDir("code");
        Directory.CreateDirectory(Path.Combine(CodePath, "Recipe"));
        Directory.CreateDirectory(Path.Combine(CodePath, "Views"));
        File.WriteAllText(Path.Combine(CodePath, "Main.cs"), new string('x', 300));
        File.WriteAllText(Path.Combine(CodePath, "Recipe", "Recipe.cs"), "recipe");



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
        SettingsEditor = new FakeSettingsEditor();
        Engine = new FileOperationEngine(RecycleBin);
        SelfLauncher = new FakeSelfLauncher();
        Settings = settings;

        ViewModel = new MainViewModel(
            Workspace, Store, new FolderEnumerator(), Shell, Clipboard, Engine, Prompt, MacroEditor,
            FolderEditor, SelfLauncher, settings, settingsWritable, SettingsEditor);
    }


    public AppSettings? Settings { get; }

    public string CodePath { get; }


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

    public FakeSettingsEditor SettingsEditor { get; }

    public FileOperationEngine Engine { get; }

    public FakeSelfLauncher SelfLauncher { get; }

    public MainViewModel ViewModel { get; }





    public FolderPanelViewModel Rotating => ViewModel.RotatingPanel!;


    public void MakeDeepTree()
    {
        foreach (var root in new[] { CodePath, WorkPath })
        {


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

        }
    }
}
