using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using FolderDeck.Core.Paths;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.ViewModels;

public sealed partial class LauncherViewModel : ObservableObject
{

    public const string DeleteNotice = "실제 폴더는 지워지지 않습니다. 작업 관리 항목만 삭제됩니다.";

    private readonly IWorkspaceStore _store;
    private readonly IFolderPicker _picker;
    private readonly IUserPrompt _prompt;
    private readonly IShellLauncher _shell;
    private readonly IInstanceSignals _signals;
    private readonly IWorkspaceHost _host;
    private readonly bool _settingsWritable;

    public LauncherViewModel(
        IWorkspaceStore store,
        AppSettings settings,
        IFolderPicker picker,
        IUserPrompt prompt,
        IShellLauncher shell,
        IInstanceSignals signals,
        IWorkspaceHost host,
        bool settingsWritable = true)
    {
        _store = store;
        _picker = picker;
        _prompt = prompt;
        _shell = shell;
        _signals = signals;
        _host = host;
        _settingsWritable = settingsWritable;
        Settings = settings;
        skipLauncher = settings.SkipLauncher;

        Refresh();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<WorkspaceCardViewModel> Cards { get; } = [];

    private readonly List<WorkspaceCardViewModel> _allCards = [];

    public bool HasNoWorkspaces => _allCards.Count == 0;

    public bool HasNoMatches => _allCards.Count > 0 && Cards.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter))]
    private string filterText = string.Empty;

    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterText);

    public bool ShowFilter => _allCards.Count > 1;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public bool CanChangeSkipLauncher => _settingsWritable;

    [ObservableProperty]
    private bool skipLauncher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    private bool isCreating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string newTitle = string.Empty;

    public ObservableCollection<string> NewFolders { get; } = [];

    partial void OnSkipLauncherChanged(bool value)
    {
        if (!_settingsWritable)
        {
            return;
        }

        Settings.SkipLauncher = value;
        _store.SaveSettingsDebounced(Settings);
    }

    public void Refresh()
    {
        var listing = _store.ListWorkspaces();

        _allCards.Clear();

        foreach (var workspace in listing.Workspaces.OrderByDescending(w => w.LastUsed))
        {
            _allCards.Add(new WorkspaceCardViewModel(workspace, _store));
        }

        foreach (var failure in listing.Failures)
        {
            _allCards.Add(new WorkspaceCardViewModel(failure));
        }

        RefreshOpenState();
        ApplyFilter();
    }

    public void RefreshOpenState()
    {
        var ids = _allCards.Where(c => c.Workspace is not null).Select(c => c.Workspace!.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var open = _signals.WhichAreOpen(ids);
        foreach (var card in _allCards)
        {
            if (card.Workspace is { } workspace)
            {
                card.IsOpen = open.Contains(workspace.Id);
            }
        }
    }

    private void ApplyFilter()
    {
        var query = FilterText?.Trim() ?? string.Empty;

        Cards.Clear();
        foreach (var card in _allCards)
        {
            if (query.Length == 0 || card.Matches(query))
            {
                Cards.Add(card);
            }
        }

        OnPropertyChanged(nameof(HasNoWorkspaces));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(ShowFilter));
    }

    [RelayCommand]
    public void Select(WorkspaceCardViewModel? card)
    {
        foreach (var other in _allCards)
        {
            other.IsSelected = ReferenceEquals(other, card);
        }
    }

    [RelayCommand]
    public void ToggleEdit(WorkspaceCardViewModel? card)
    {
        if (card is { IsUsable: true })
        {
            card.IsEditing = !card.IsEditing;
        }
    }

    [RelayCommand]
    public void RevealFile(WorkspaceCardViewModel? card)
    {
        if (card?.Failure is null)
        {
            return;
        }

        var error = _shell.RevealInExplorer(card.Failure.Path, isDirectory: false);
        if (error is not null)
        {
            Message = $"탐색기에서 열지 못했다: {error}";
        }
    }

    [RelayCommand]
    public void Open(WorkspaceCardViewModel? card)
    {
        if (card?.Workspace is null)
        {
            return;
        }

        var result = _host.Open(card.Workspace.Id);
        switch (result.Outcome)
        {
            case WorkspaceOpenOutcome.Opened:
                card.IsOpen = true;
                Message = null;
                break;
            case WorkspaceOpenOutcome.AlreadyOpen:
                card.IsOpen = true;
                Message = "그 작업 관리는 이미 열려 있다.";
                break;
            default:
                card.IsOpen = false;
                Message = $"열지 못했다: {result.Message}";
                break;
        }
    }

    [RelayCommand]
    public void Delete(WorkspaceCardViewModel? card)
    {
        if (card?.Workspace is null)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(card.Title) ? "(제목 없음)" : card.Title;
        if (!_prompt.Confirm("작업 관리 삭제", $"'{name}' 을(를) 삭제할까요?\n\n{DeleteNotice}"))
        {
            return;
        }

        var failure = _store.DeleteWorkspace(card.Workspace.Id);
        if (failure is not null)
        {
            Message = $"삭제 실패: {failure.Message}";
            return;
        }

        if (_settingsWritable && Settings.LastWorkspaceId == card.Workspace.Id)
        {
            Settings.LastWorkspaceId = null;
            _store.SaveSettingsDebounced(Settings);
        }

        Message = null;
        Refresh();
    }

    [RelayCommand]
    public void StartCreate()
    {
        NewTitle = string.Empty;
        NewFolders.Clear();
        Message = null;
        IsCreating = true;
    }

    [RelayCommand]
    public void CancelCreate()
    {
        IsCreating = false;
        NewTitle = string.Empty;
        NewFolders.Clear();
    }

    [RelayCommand]
    public void AddFoldersFromPicker()
    {
        AddFolders(_picker.PickFolders("작업 관리에 넣을 폴더 선택"));
    }

    public void AddFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = FolderPathRules.Normalize(path);

            if (!NewFolders.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                NewFolders.Add(normalized);
            }
        }

        CreateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void RemoveNewFolder(string? path)
    {
        if (path is not null)
        {
            NewFolders.Remove(path);
            CreateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanCreate => !string.IsNullOrWhiteSpace(NewTitle) && NewFolders.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    public void Create()
    {
        var workspace = new Workspace
        {
            Title = NewTitle.Trim(),
            LastUsed = DateTimeOffset.Now,
            Folders = [.. NewFolders.Select((path, index) => new FolderEntry
            {
                Path = path,
                DisplayName = FolderPathRules.LeafName(path),

                Pinned = index == 0,
            })],
        };

        var failure = _store.SaveWorkspace(workspace);
        if (failure is not null)
        {
            Message = $"만들지 못했다: {failure.Message}";
            return;
        }

        IsCreating = false;
        NewTitle = string.Empty;
        NewFolders.Clear();
        Refresh();
    }
}
