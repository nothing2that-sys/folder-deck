using System.Threading.Tasks;
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

public partial class App : Application, IWorkspaceHost
{
    private WorkspaceStore? _store;
    private AppSettings _settings = new();
    private bool _settingsWritable = true;

    /// <summary>이 프로세스가 들고 있는 것(워크스페이스 하나 또는 런처) (§6.2).</summary>
    private InstanceLock? _instanceLock;

    /// <summary>"앞으로" 신호를 받는 파이프 서버 (§3.3).</summary>
    private ActivationListener? _listener;

    /// <summary>시작할 때 1회용 창 단축키를 바로가기에 못 맞췄으면 그 이유(S4). 띄운 창이 한 줄로 알린다.</summary>
    private string? _shortcutSyncFailure;

    /// <summary>떠 있는 선택 창 (§3.1). 워크스페이스를 열 때 이 창을 닫고 갈아끼운다.</summary>
    private LauncherWindow? _launcherWindow;

    /// <summary>워크스페이스를 열려고 했을 때 어디로 갈라졌는가.</summary>
    private enum OpenOutcome
    {
        /// <summary>창을 띄웠다. 이 프로세스가 그 워크스페이스를 든다.</summary>
        Shown,

        /// <summary>열지 않았다(이미 열려 있거나, 파일이 없거나 손상). 선택 창으로 떨어진다.</summary>
        Failed,
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 전역 안전망(검토 회차 발견) — 이 앱에는 지금까지 미처리 예외 핸들러도 파일
        // 로깅도 전혀 없었다. 여기서 하는 일은 로그를 남기는 것뿐이고, 크래시 자체를
        // 막거나 계속 돌리려 하지 않는다 — 상태가 어디까지 깨졌는지 모르는 채로 앱을
        // 계속 돌리는 쪽이 조용히 죽는 것보다 나쁠 수 있다(CrashLogger 주석 참고).
        DispatcherUnhandledException += (_, args) =>
            CrashLogger.TryWrite("DispatcherUnhandledException", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CrashLogger.TryWrite("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            CrashLogger.TryWrite("TaskScheduler.UnobservedTaskException", args.Exception);

        var options = CommandLineOptions.Parse(e.Args);

        var store = new WorkspaceStore();
        _store = store;
        store.Paths.EnsureCreated();

        LoadSettings(store);

        string? message = options.Error;

        // 1회용 창 단축키를 설치본 바로가기에 맞춘다(2026-10-14 S4). 설정값이 원본이다 — 재설치가
        // 바로가기를 기본값으로 다시 만들었어도 여기서 고친다. 시작을 막지 않는다: 실패는 띄운 창에
        // 한 줄로만 알린다. 바로가기가 없으면(개발 빌드) 아무 일도 없다.
        var shortcutSync = ScratchShortcut.Default.Apply(_settings.ScratchHotkey);
        _shortcutSyncFailure = shortcutSync.Outcome == ScratchShortcutOutcome.Failed ? shortcutSync.Error : null;

        // ⓪ 1회용 창(S4) — 뮤텍스·파이프를 잡지 않으므로 여러 개가 동시에 뜬다. skipLauncher 와 무관하다.
        if (options.Scratch)
        {
            ShowScratchWindow();
            return;
        }

        // ① 인자로 워크스페이스를 지정했다 — 선택 창을 거치지 않는다 (§3.1).
        if (options.WorkspaceId is { } requested)
        {
            switch (OpenWorkspace(requested, ref message))
            {
                case OpenOutcome.Shown:
                    return;
                default:
                    break;
            }
        }

        // ② 자동 열기가 켜져 있고 대상이 유효하면 선택 창을 건너뛴다 (§3.1).
        //    --launcher 는 이 건너뛰기를 무시하라는 요청이다.
        else if (!options.ForceLauncher && _settings.SkipLauncher && _settings.LastWorkspaceId is { } last)
        {
            switch (OpenWorkspace(last, ref message))
            {
                case OpenOutcome.Shown:
                    return;
                default:
                    break;
            }
        }

        ShowLauncher(message);
    }

    /// <summary>
    /// 워크스페이스 선택 창을 띄운다 (§3.1). 하나를 고르면 이 창이 그대로 그 워크스페이스
    /// 창이 되므로(<see cref="Open"/>) 상주하지 않는다 — 인스턴스 하나 규칙은 워크스페이스
    /// 쪽에만 남는다 (§6.2).
    /// </summary>
    /// <param name="message">잘못된 인자 등 시작 시점의 알릴 것. 없으면 null.</param>
    private void ShowLauncher(string? message)
    {
        // 워크스페이스가 0개여도 선택 창은 뜬다 — 여기서 첫 묶음을 만들 수 있어야 하므로.
        var viewModel = new LauncherViewModel(
            _store!,
            _settings,
            new FolderPicker(),
            new UserPrompt(),
            new ShellLauncher(),
            new InstanceSignals(),
            this,
            _settingsWritable);

        var window = new LauncherWindow(viewModel);
        _launcherWindow = window;
        MainWindow = window;
        window.Show();

        if (message is not null)
        {
            viewModel.Message = message;
        }
    }

    /// <summary>
    /// 시작하자마자 워크스페이스를 연다 (§3.1). 열지 않았으면 이유를
    /// <paramref name="message"/> 에 담아 선택 창으로 떨어뜨린다 — 아무 창도 안 뜨고
    /// 끝나는 것이 §5 위반이기 때문이다.
    /// </summary>
    private OpenOutcome OpenWorkspace(Guid workspaceId, ref string? message)
    {
        var result = Open(workspaceId);
        if (result.Outcome == WorkspaceOpenOutcome.Opened)
        {
            return OpenOutcome.Shown;
        }

        message = result.Outcome == WorkspaceOpenOutcome.AlreadyOpen
            ? "그 작업 관리는 이미 열려 있어 열지 않았다."
            : $"열려던 작업 관리를 읽지 못해 목록을 띄운다: {result.Message}";

        return OpenOutcome.Failed;
    }

    /// <inheritdoc/>
    public WorkspaceOpenResult Open(Guid workspaceId)
    {
        var result = _store!.LoadWorkspace(workspaceId);
        if (result.Value is null)
        {
            // 대상이 사라졌거나 손상됐다. 선택 창이 살아 있으므로 창을 띄우지 않고 Message 로
            // 알린다 (§5).
            return WorkspaceOpenResult.Failed($"{result.Failure!.Path} — {result.Failure.Message}");
        }

        var workspace = result.Value;

        // 워크스페이스당 인스턴스 하나 (§6.2). 버려진 뮤텍스는 InstanceLock 이 인수한다.
        _instanceLock = InstanceLock.TryAcquire(InstanceNames.WorkspaceMutex(workspaceId));

        var pipeName = InstanceNames.WorkspacePipe(workspaceId);
        if (_instanceLock is null && InstanceSignals.IsLive(pipeName))
        {
            // 이미 열려 있다 — 아무 일도 하지 않는다 (§6.2).
            return WorkspaceOpenResult.AlreadyOpen();
        }

        // 뮤텍스는 잡혀 있는데 파이프 이름이 없다(좀비) → 막지 않고 연다. 못 열게 하면 사용자에게
        // 빠져나갈 길이 없다. 대신 §5.1 "실패·경고"로 남긴다 (§6.2).
        var duplicate = _instanceLock is null;

        var window = ShowWorkspaceWindow(workspace);
        StartListener(pipeName, window);

        // 새 창을 띄운 뒤에 선택 창을 닫는다. App.xaml 에 ShutdownMode 가 없어 기본값
        // OnLastWindowClose 이므로, 먼저 닫으면 창이 0개가 되어 앱이 그대로 끝난다.
        _launcherWindow?.Close();
        _launcherWindow = null;

        if (duplicate)
        {
            window.ViewModel.ReportDuplicateWindow();
        }
        else if (_listener?.Failure is { } failure)
        {
            window.ViewModel.ReportNoSignal(failure);
        }

        return WorkspaceOpenResult.Opened();
    }

    /// <summary>
    /// 1회용 창을 띄운다 (2026-10-14 S4). 작업 관리는 메모리에서만 만든다 — 저장소를 래퍼로 감싸 그
    /// 작업 관리의 저장을 막는다(<see cref="ScratchWorkspaceStore"/>). <see cref="Open"/> 의
    /// 뮤텍스·파이프 경로를 타지 않는다: 여러 개를 허용하고(JH), 신호로 앞으로 불러올 이름도 없다.
    /// </summary>
    private void ShowScratchWindow()
    {
        var workspace = new Workspace
        {
            Title = "1회용",
            Folders = [],
            Tiles = LayoutPresets.Build(_settings.ScratchLayout),
        };

        ShowWorkspaceWindow(workspace, new ScratchWorkspaceStore(_store!, workspace.Id), isScratch: true);
    }

    /// <summary>워크스페이스 창을 실제로 만든다.</summary>
    /// <param name="storeOverride">1회용 창이면 저장을 막는 래퍼. 아니면 null(앱 저장소).</param>
    /// <param name="isScratch">
    /// 1회용 창인가. 그러면 <c>LastWorkspaceId</c> 를 바꾸지 않는다 — 1회용의 id 가 「마지막 작업 관리」로
    /// 남으면 다음 자동 열기가 없는 파일을 찾는다.
    /// </param>
    private MainWindow ShowWorkspaceWindow(Workspace workspace, IWorkspaceStore? storeOverride = null, bool isScratch = false)
    {
        if (!isScratch && _settingsWritable && _settings.LastWorkspaceId != workspace.Id)
        {
            _settings.LastWorkspaceId = workspace.Id;
            _store!.SaveSettingsDebounced(_settings);
        }

        var viewModel = new MainViewModel(
            workspace,
            storeOverride ?? _store!,
            new FolderEnumerator(),
            new ShellLauncher(),
            new ClipboardService(),
            new FileOperationEngine(new RecycleBin()),
            new UserPrompt(),
            new MacroEditor(),
            new FolderEditor(),
            new SelfLauncher(),
            _settings,
            _settingsWritable,
            new IconSettingsEditor(),
            new FavoritesStore(_store!.Paths),
            isScratch,
            ScratchShortcut.Default);

        var window = new MainWindow(viewModel, new FolderPicker());

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

        if (_shortcutSyncFailure is { } shortcutFailure)
        {
            viewModel.ReportShortcutSyncFailure(shortcutFailure);
        }

        return window;
    }

    /// <summary>
    /// 파이프 신호를 받으면 창을 앞으로 가져온다. 수신은 UI 스레드가 아닌 곳에서 일어나므로
    /// 디스패처로 마샬한다 (§3.3).
    /// </summary>
    private void StartListener(string pipeName, Window window)
    {
        _listener = new ActivationListener(
            pipeName,
            () => window.Dispatcher.BeginInvoke(() => WindowActivation.BringToFront(window)));
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

        base.OnExit(e);
    }
}
