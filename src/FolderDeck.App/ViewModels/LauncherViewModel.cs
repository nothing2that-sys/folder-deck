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

/// <summary>런처 화면 (§3.1).</summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    /// <summary>실제 폴더는 건드리지 않는다는 것을 확인창 문구에 못박는다 (§3.1).</summary>
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

        RefreshOpenState();
        ApplyFilter();
    }

    /// <summary>
    /// "열려 있음" 배지를 다시 잰다 (§4.2). <b>폴링하지 않는다</b> — 목록을 다시 읽을 때와
    /// 런처 창이 활성화될 때만 부른다(§8 의 "수동 F5 + 창 활성화 시 재조회"와 같은 결).
    /// </summary>
    /// <remarks>
    /// 파이프 이름공간을 한 번 열거하는 비용이라 워크스페이스 개수에 거의 무관하다
    /// (실측: 등록 50개 · 이름공간 1000여 개에서 3~4ms).
    /// </remarks>
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

    /// <summary>
    /// "열기" — 고른 워크스페이스를 <b>이 프로세스에서</b> 연다 (§3.1). 이 창이 그대로 그
    /// 워크스페이스 창이 되므로 새 프로세스를 띄우지 않는다.
    /// </summary>
    /// <remarks>
    /// <b>이미 열려 있으면 아무 일도 하지 않는다</b> (§6.2). 판정은 <see cref="IWorkspaceHost"/>
    /// 가 뮤텍스와 파이프 이름으로 하고, 여기서는 그 결과로 배지만 맞춘다 — 그래서 배지가
    /// 낡아 있어도(§4.2 "힌트다") 동작은 어긋나지 않는다.
    /// </remarks>
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
}
