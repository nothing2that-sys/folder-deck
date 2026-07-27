using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;
using IoPath = System.IO.Path;

namespace FolderDeck.App.ViewModels;

/// <summary>런처 화면 (§3.1).</summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    /// <summary>실제 폴더는 건드리지 않는다는 것을 확인창 문구에 못박는다 (§3.1).</summary>
    public const string DeleteNotice = "실제 폴더는 지워지지 않습니다. 작업 관리 항목만 삭제됩니다.";

    private readonly IWorkspaceStore _store;
    private readonly IFolderPicker _picker;
    private readonly IUserPrompt _prompt;
    private readonly IShellLauncher _shell;
    private readonly Action<Workspace> _openWorkspace;
    private readonly bool _settingsWritable;

    public LauncherViewModel(
        IWorkspaceStore store,
        AppSettings settings,
        Action<Workspace> openWorkspace,
        IFolderPicker picker,
        IUserPrompt prompt,
        IShellLauncher shell,
        bool settingsWritable = true)
    {
        _store = store;
        _picker = picker;
        _prompt = prompt;
        _shell = shell;
        _openWorkspace = openWorkspace;
        _settingsWritable = settingsWritable;
        Settings = settings;
        skipLauncher = settings.SkipLauncher;

        Refresh();
    }

    public AppSettings Settings { get; }

    /// <summary>필터를 통과해 지금 보이는 카드. 마지막 사용 최신순, 읽지 못한 파일은 맨 뒤.</summary>
    public ObservableCollection<WorkspaceCardViewModel> Cards { get; } = [];

    /// <summary>필터 이전의 전체 목록.</summary>
    private readonly List<WorkspaceCardViewModel> _allCards = [];

    /// <summary>저장된 워크스페이스가 하나도 없다(필터 때문에 빈 것과 구분한다).</summary>
    public bool HasNoWorkspaces => _allCards.Count == 0;

    /// <summary>필터를 걸었는데 걸린 게 없다.</summary>
    public bool HasNoMatches => _allCards.Count > 0 && Cards.Count == 0;

    /// <summary>제목·경로로 좁히는 한 줄 필터. 즉석 부분일치(대소문자 무시), I/O 없음.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilter))]
    private string filterText = string.Empty;

    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterText);

    /// <summary>필터 입력줄은 카드가 여럿일 때만 값이 있다.</summary>
    public bool ShowFilter => _allCards.Count > 1;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>설정 파일이 손상됐으면 덮어쓰지 않으므로 토글을 잠근다 (§5).</summary>
    public bool CanChangeSkipLauncher => _settingsWritable;

    /// <summary>"마지막 작업을 자동으로 열기(런처 건너뛰기)" (§3.1).</summary>
    [ObservableProperty]
    private bool skipLauncher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    // ── 새로 만들기 (인라인) ─────────────────────────────────────────────────

    [ObservableProperty]
    private bool isCreating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string newTitle = string.Empty;

    /// <summary>새 워크스페이스에 넣을 폴더. 최소 1개 있어야 만들 수 있다.</summary>
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

        // 손상된 파일도 남긴다 — 목록에서 지우면 사라진 줄 안다 (§5).
        foreach (var failure in listing.Failures)
        {
            _allCards.Add(new WorkspaceCardViewModel(failure));
        }

        ApplyFilter();
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

    /// <summary>"수정" — 카드를 편집 모드로. 값은 바뀔 때마다 저장되므로 따로 커밋하지 않는다.</summary>
    [RelayCommand]
    public void ToggleEdit(WorkspaceCardViewModel? card)
    {
        if (card is { IsUsable: true })
        {
            card.IsEditing = !card.IsEditing;
        }
    }

    /// <summary>손상 카드의 유일한 액션 — 탐색기에서 그 파일을 선택해 보여준다.</summary>
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

        _openWorkspace(card.Workspace);
    }

    /// <summary>
    /// 워크스페이스 JSON 만 지운다. 등록된 실제 폴더는 손대지 않는다 (§3.1).
    /// </summary>
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

        // 지운 것을 자동 열기 대상으로 남겨두면 다음 실행이 없는 파일을 열려 한다.
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

    /// <summary>창에 폴더를 끌어다 놓은 경우에도 같은 경로를 쓴다.</summary>
    public void AddFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
            if (normalized.Length == 0)
            {
                normalized = path;
            }

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
                DisplayName = LeafName(path),

                // 첫 폴더만 상시로 둔다 — 안 그러면 새 워크스페이스를 열었을 때
                // 오른쪽에 빈 순환 칸 하나만 보인다. 상시/순환 지정 UI 는 Phase 2 다.
                Pinned = index == 0,
            })],
        };

        // 목록에 바로 보여야 하므로 디바운스가 아니라 지금 쓴다.
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

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        var name = IoPath.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
