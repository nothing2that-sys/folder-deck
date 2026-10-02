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


    private InstanceLock? _instanceLock;


    private ActivationListener? _listener;


    private LauncherWindow? _launcherWindow;


    private enum OpenOutcome
    {

        Shown,


        Failed,
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);





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







    private void ShowLauncher(string? message)
    {

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


    public WorkspaceOpenResult Open(Guid workspaceId)
    {
        var result = _store!.LoadWorkspace(workspaceId);
        if (result.Value is null)
        {


            return WorkspaceOpenResult.Failed($"{result.Failure!.Path} — {result.Failure.Message}");
        }

        var workspace = result.Value;


        _instanceLock = InstanceLock.TryAcquire(InstanceNames.WorkspaceMutex(workspaceId));

        var pipeName = InstanceNames.WorkspacePipe(workspaceId);
        if (_instanceLock is null && InstanceSignals.IsLive(pipeName))
        {

            return WorkspaceOpenResult.AlreadyOpen();
        }



        var duplicate = _instanceLock is null;

        var window = ShowWorkspaceWindow(workspace);
        StartListener(pipeName, window);



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
            new FolderEditor(),
            new SelfLauncher(),
            _settings,
            _settingsWritable,
            new IconSettingsEditor());

        var window = new MainWindow(viewModel, new FolderPicker());




        var monitors = MonitorLayout.Query();
        var placement = WindowPlacement.Resolve(workspace.Window, monitors.WorkAreas, monitors.PrimaryIndex);
        window.ApplyPlacement(placement);

        MainWindow = window;
        window.Show();


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





    private void StartListener(string pipeName, Window window)
    {
        _listener = new ActivationListener(
            pipeName,
            () => window.Dispatcher.BeginInvoke(() => WindowActivation.BringToFront(window)));
    }





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

        _listener?.Dispose();
        _store?.Dispose();
        _instanceLock?.Dispose();

        base.OnExit(e);
    }
}
