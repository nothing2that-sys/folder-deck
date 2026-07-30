using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;

namespace FolderDeck.App;

public partial class App : Application
{
    private WorkspaceStore? _store;
    private AppSettings _settings = new();
    private bool _settingsWritable = true;

    /// <summary>이 프로세스가 들고 있는 것(워크스페이스 하나 또는 런처) (§6.2).</summary>
    private InstanceLock? _instanceLock;

    /// <summary>"앞으로" 신호를 받는 파이프 서버 (§3.3).</summary>
    private ActivationListener? _listener;

    /// <summary>
    /// 이 프로세스가 워크스페이스 창을 들고 있는가. 끝나면서 런처에게 알릴지를 여기서 가른다 —
    /// 런처 자신은 알릴 필요가 없다.
    /// </summary>
    private bool _holdsWorkspace;

    /// <summary>워크스페이스를 열려고 했을 때 어디로 갈라졌는가.</summary>
    private enum OpenOutcome
    {
        /// <summary>창을 띄웠다. 이 프로세스가 그 워크스페이스를 든다.</summary>
        Shown,

        /// <summary>이미 열려 있는 창에 넘겼다. 이 프로세스는 할 일이 없다 (§3.2-3).</summary>
        HandedOff,

        /// <summary>열지 못했다(파일이 없거나 손상). 런처로 떨어진다.</summary>
        Failed,
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = CommandLineOptions.Parse(e.Args);

        var store = new WorkspaceStore();
        _store = store;
        store.Paths.EnsureCreated();

        LoadSettings(store);

        // ① 인자로 워크스페이스를 지정했다 — 런처를 거치지 않는다 (§3.1).
        if (options.WorkspaceId is { } requested)
        {
            switch (OpenWorkspace(requested))
            {
                case OpenOutcome.Shown:
                    return;
                case OpenOutcome.HandedOff:
                    Shutdown(0);
                    return;
                default:
                    break;
            }
        }

        // ② 자동 열기가 켜져 있고 대상이 유효하면 런처를 건너뛴다 (§3.1).
        //    --launcher 는 이 건너뛰기를 무시하라는 요청이다.
        else if (!options.ForceLauncher && _settings.SkipLauncher && _settings.LastWorkspaceId is { } last)
        {
            switch (OpenWorkspace(last))
            {
                case OpenOutcome.Shown:
                    return;
                case OpenOutcome.HandedOff:
                    Shutdown(0);
                    return;
                default:
                    break;
            }
        }

        ShowLauncher(options.Error);
    }

    /// <summary>
    /// 런처를 띄운다. 런처도 하나뿐이다 (§4.3) — 두 벌이 뜨면 카드 배지가 서로 달라져
    /// "힌트"가 신뢰를 잃는다.
    /// </summary>
    /// <param name="message">잘못된 인자 등 시작 시점의 알릴 것. 없으면 null.</param>
    private void ShowLauncher(string? message)
    {
        var launcherLock = InstanceLock.TryAcquire(InstanceNames.LauncherMutex());
        if (launcherLock is null && ActivationSignal.TrySend(InstanceNames.LauncherPipe()))
        {
            // 기존 런처가 앞으로 나왔다. 사용자가 요청한 결과가 화면에 있으므로 조용히 끝낸다.
            Shutdown(0);
            return;
        }

        _instanceLock = launcherLock;

        // 워크스페이스가 0개여도 런처는 뜬다 — 여기서 첫 묶음을 만들 수 있어야 하므로.
        var viewModel = new LauncherViewModel(
            _store!,
            _settings,
            new FolderPicker(),
            new UserPrompt(),
            new ShellLauncher(),
            new InstanceSignals(),
            new SelfLauncher(),
            _settingsWritable);

        var window = new LauncherWindow(viewModel);
        MainWindow = window;
        window.Show();

        // 런처는 "앞으로" 신호 말고 "누가 닫혔다"도 받는다 — 그때 배지를 다시 잰다.
        // 창 활성화만으로는 배지가 낡은 채 남는 경로가 둘 있다(ActivationSignal.ClosedPayload).
        StartListener(
            InstanceNames.LauncherPipe(),
            window,
            onPeerClosed: () => window.Dispatcher.BeginInvoke(viewModel.RefreshOpenState));

        // 좀비 런처(뮤텍스는 잡혀 있는데 응답이 없다)여서 두 벌이 됐다면 조용히 넘기지 않는다 (§5).
        if (launcherLock is null)
        {
            message = LauncherViewModel.DuplicateLauncherWarning;
        }
        else if (_listener?.Failure is { } failure)
        {
            message = LauncherViewModel.NoSignalWarning(failure);
        }

        if (message is not null)
        {
            viewModel.Message = message;
        }
    }

    /// <summary>
    /// 워크스페이스 창을 이 프로세스에서 연다. 이미 열려 있으면 그 창을 앞으로 보내고
    /// <see cref="OpenOutcome.HandedOff"/> 를 돌려준다 (§3.2).
    /// </summary>
    private OpenOutcome OpenWorkspace(Guid workspaceId)
    {
        var result = _store!.LoadWorkspace(workspaceId);
        if (result.Value is null)
        {
            // 대상이 사라졌거나 손상됐다. 조용히 넘기지 않고 알린 뒤 런처로 떨어진다 (§5).
            MessageBox.Show(
                "열려던 작업 관리를 읽지 못해 목록을 띄운다.\n\n" +
                $"{result.Failure!.Path}\n{result.Failure.Message}",
                "FolderDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return OpenOutcome.Failed;
        }

        var workspace = result.Value;

        // 워크스페이스당 인스턴스 하나 (§6.2). 버려진 뮤텍스는 InstanceLock 이 인수한다.
        _instanceLock = InstanceLock.TryAcquire(InstanceNames.WorkspaceMutex(workspaceId));

        var pipeName = InstanceNames.WorkspacePipe(workspaceId);
        if (_instanceLock is null && ActivationSignal.TrySend(pipeName))
        {
            return OpenOutcome.HandedOff;
        }

        // 뮤텍스는 잡혀 있는데 신호가 안 통한다(좀비) → 막지 않고 연다. 못 열게 하면 사용자에게
        // 빠져나갈 길이 없다. 대신 §5.1 "실패·경고"로 남긴다 (§3.2-3).
        var duplicate = _instanceLock is null;

        var window = ShowWorkspaceWindow(workspace);
        _holdsWorkspace = true;
        StartListener(pipeName, window);

        if (duplicate)
        {
            window.ViewModel.ReportDuplicateWindow();
        }
        else if (_listener?.Failure is { } failure)
        {
            window.ViewModel.ReportNoSignal(failure);
        }

        return OpenOutcome.Shown;
    }

    /// <summary>워크스페이스 창을 실제로 만든다.</summary>
    private MainWindow ShowWorkspaceWindow(Workspace workspace)
    {
        if (_settingsWritable && _settings.LastWorkspaceId != workspace.Id)
        {
            _settings.LastWorkspaceId = workspace.Id;
            _store!.SaveSettingsDebounced(_settings);
        }

        var viewModel = new MainViewModel(
            workspace,
            _store!,
            new FolderEnumerator(),
            new ShellLauncher(),
            new ClipboardService(),
            new FileOperationEngine(new RecycleBin()),
            new UserPrompt(),
            new MacroEditor(),
            new SelfLauncher());

        var window = new MainWindow(viewModel);

        // 저장된 자리로 띄운다. 되찾을 수 없는 자리면 여기서 기본 위치로 바뀐다 (§6.1).
        // 이 판정은 창을 만들기 전에 도는 코드지만, 매니페스트가 DPI 인식을 프로세스 시작
        // 시점에 못박아 두어 창 전후의 좌표계가 같다 (app.manifest 주석).
        var monitors = MonitorLayout.Query();
        var placement = WindowPlacement.Resolve(workspace.Window, monitors.WorkAreas, monitors.PrimaryIndex);
        window.ApplyPlacement(placement);

        MainWindow = window;
        window.Show();

        // 자리를 바꿨으면 밝힌다 — "내가 둔 자리가 아닌데" 하는 순간을 없앤다 (§5.1 정보).
        switch (placement.Decision)
        {
            case WindowPlacementDecision.Fallback:
                viewModel.ReportWindowMoved();
                break;
            case WindowPlacementDecision.Resized:
                viewModel.ReportWindowResized();
                break;
            case WindowPlacementDecision.MinimumEnforced:
                viewModel.ReportWindowAtMinimum();
                break;
            default:
                break;
        }

        return window;
    }

    /// <summary>
    /// 파이프 신호를 받으면 창을 앞으로 가져온다. 수신은 UI 스레드가 아닌 곳에서 일어나므로
    /// 디스패처로 마샬한다 (§3.3).
    /// </summary>
    private void StartListener(string pipeName, Window window, Action? onPeerClosed = null)
    {
        _listener = new ActivationListener(
            pipeName,
            () => window.Dispatcher.BeginInvoke(() => WindowActivation.BringToFront(window)),
            onPeerClosed);
    }

    /// <summary>
    /// 파일이 없으면(첫 실행) 기본값을 쓴다. 손상된 경우엔 <b>덮어쓰지 않고</b> 알린다 —
    /// 조용한 초기화가 §5 위반이기 때문이다.
    /// </summary>
    private void LoadSettings(IWorkspaceStore store)
    {
        var result = store.LoadSettings();
        if (result.Value is not null)
        {
            _settings = result.Value;
            return;
        }

        if (result.Failure!.Kind == StorageFailureKind.NotFound)
        {
            _settings = new AppSettings();
            return;
        }

        MessageBox.Show(
            "설정 파일을 읽을 수 없다. 덮어쓰지 않고 그대로 두었다.\n" +
            "이번 실행에서는 설정 변경(자동 열기 등)이 저장되지 않는다.\n\n" +
            $"{result.Failure.Path}\n{result.Failure.Message}",
            "FolderDeck",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        _settings = new AppSettings();
        _settingsWritable = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 파이프 서버를 닫고 뮤텍스를 놓는다 (§3.2). 놓기 전에 밀린 저장을 마무리한다 (§5).
        _listener?.Dispose();
        _store?.Dispose();
        _instanceLock?.Dispose();

        NotifyLauncherThisWindowClosed();

        base.OnExit(e);
    }

    /// <summary>
    /// 떠 있는 런처에게 "이 창은 닫혔다"를 알린다 — 런처가 배지를 다시 잰다 (§4.2).
    /// </summary>
    /// <remarks>
    /// <b>자기 파이프를 닫은 뒤에 알리는 것이 핵심이다.</b> 먼저 알리면 런처가 아직 살아 있는
    /// 이 프로세스의 파이프를 보고 "열려 있음"으로 다시 판정한다 — 고치려던 것과 같은 상태가 된다.
    /// <para>
    /// 런처가 없으면 보내지 않는다(존재 확인은 부작용 없는 열거다). 없는 상대에게 보내면
    /// 연결 한도만큼 종료가 늦어지는데, 그 대가로 얻는 것이 없다.
    /// </para>
    /// </remarks>
    private void NotifyLauncherThisWindowClosed()
    {
        if (!_holdsWorkspace)
        {
            return;
        }

        var launcherPipe = InstanceNames.LauncherPipe();
        if (InstanceSignals.IsLive(launcherPipe))
        {
            // 실패해도 할 것이 없다 — 배지가 다음 활성화까지 낡아 있을 뿐이다.
            ActivationSignal.TryNotifyClosed(launcherPipe);
        }
    }
}
