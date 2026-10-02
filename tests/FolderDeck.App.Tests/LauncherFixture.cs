using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using FolderDeck.Core.Renaming;
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


    public List<string> Reports { get; } = [];

    public bool Confirm(string title, string message)
    {
        Messages.Add(message);
        return Answer;
    }

    public void Report(string title, string message) => Reports.Add(message);




    public List<string> ConflictMessages { get; } = [];





    public ConflictDecision? ConflictAnswer { get; set; } =
        new(ConflictPolicy.Overwrite, Remember: false);


    public ConflictPolicy? SuggestedPolicy { get; private set; }

    public ConflictDecision? AskConflict(string title, string message, ConflictPolicy suggested)
    {
        ConflictMessages.Add(message);
        SuggestedPolicy = suggested;
        return ConflictAnswer;
    }







    public string? NameAnswer { get; set; }


    public List<string> NamesAsked { get; } = [];





    public List<string> AcceptButtonTextsAsked { get; } = [];

    public string? AskName(string title, string message, string current, string acceptButtonText)
    {
        NamesAsked.Add(current);
        AcceptButtonTextsAsked.Add(acceptButtonText);
        return NameAnswer;
    }




    public int? PickAnswer { get; set; }


    public List<IReadOnlyList<string>> PickOptionsAsked { get; } = [];






    public Action? OnPickOneCalled { get; set; }

    public int? PickOne(string title, string message, IReadOnlyList<string> options)
    {
        PickOptionsAsked.Add(options);
        OnPickOneCalled?.Invoke();
        return PickAnswer;
    }




    public IReadOnlyList<RenamePreviewRow>? BatchRenameAnswer { get; set; }


    public List<(IReadOnlyList<string> OriginalNames, IReadOnlyCollection<string> OtherExistingNames)>
        BatchRenameAsked { get; } = [];

    public IReadOnlyList<RenamePreviewRow>? PlanBatchRename(
        IReadOnlyList<string> originalNames, IReadOnlyCollection<string> otherExistingNames)
    {
        BatchRenameAsked.Add((originalNames, otherExistingNames));
        return BatchRenameAnswer;
    }
}


internal sealed class FakeFolderEditor : IFolderEditor
{

    public FolderEditDraft? Answer { get; set; }


    public List<FolderEditDraft> Opened { get; } = [];


    public string? OpenedPath { get; private set; }

    public FolderEditDraft? Edit(FolderEditDraft draft, string path)
    {
        Opened.Add(draft);
        OpenedPath = path;
        return Answer;
    }
}


internal sealed class FakeSettingsEditor : ISettingsEditor
{

    public SettingsEditResult? Answer { get; set; }


    public List<SettingsEditRequest> Opened { get; } = [];

    public SettingsEditResult? Edit(SettingsEditRequest request)
    {
        Opened.Add(request);
        return Answer;
    }
}


internal sealed class FakeMacroEditor : IMacroEditor
{

    public MacroDraft? Answer { get; set; }


    public MacroDraft? Suggested { get; private set; }

    public bool? CanFixPath { get; private set; }


    public bool? WasEdit { get; private set; }

    public int CallCount { get; private set; }


    public Func<MacroDraft, MacroDraft>? Transform { get; set; }

    public MacroDraft? Edit(MacroDraft draft, bool canFixPath, bool isEdit)
    {
        CallCount++;
        Suggested = draft;
        CanFixPath = canFixPath;
        WasEdit = isEdit;

        return Transform is not null ? Transform(draft) : Answer;
    }
}





internal sealed class FakeInstanceSignals : IInstanceSignals
{

    public HashSet<Guid> Listening { get; } = [];


    public List<Guid> Sent { get; } = [];


    public int QueryCount { get; private set; }

    public IReadOnlyCollection<Guid> WhichAreOpen(IEnumerable<Guid> candidates)
    {
        QueryCount++;
        return [.. candidates.Where(Listening.Contains)];
    }

    public bool TrySendActivate(Guid workspaceId)
    {
        Sent.Add(workspaceId);
        return Listening.Contains(workspaceId);
    }
}


internal sealed class FakeSelfLauncher : ISelfLauncher
{
    public List<Guid> Workspaces { get; } = [];

    public int LauncherCount { get; private set; }

    public string? Error { get; set; }

    public string? OpenWorkspace(Guid workspaceId)
    {
        if (Error is not null)
        {
            return Error;
        }

        Workspaces.Add(workspaceId);
        return null;
    }

    public string? OpenLauncher()
    {
        if (Error is not null)
        {
            return Error;
        }

        LauncherCount++;
        return null;
    }
}





internal sealed class FakeWorkspaceHost : IWorkspaceHost
{

    public List<Guid> Opened { get; } = [];


    public WorkspaceOpenResult NextResult { get; set; } = WorkspaceOpenResult.Opened();

    public WorkspaceOpenResult Open(Guid workspaceId)
    {
        if (NextResult.Outcome == WorkspaceOpenOutcome.Opened)
        {
            Opened.Add(workspaceId);
        }

        return NextResult;
    }
}


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
        Signals = new FakeInstanceSignals();
        Host = new FakeWorkspaceHost();
        ViewModel = new LauncherViewModel(
            Store, Settings, Picker, Prompt, Shell, Signals, Host, settingsWritable);
    }

    public FolderDeckPaths Paths { get; }

    public WorkspaceStore Store { get; }

    public AppSettings Settings { get; }

    public FakeFolderPicker Picker { get; }

    public FakeUserPrompt Prompt { get; }

    public FakeShellLauncher Shell { get; }

    public FakeInstanceSignals Signals { get; }

    public FakeWorkspaceHost Host { get; }


    public List<Guid> Opened => Host.Opened;

    public LauncherViewModel ViewModel { get; }


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

        }
    }
}
