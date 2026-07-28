using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Storage;

namespace FolderDeck.App;

public partial class App : Application
{
    private WorkspaceStore? _store;
    private AppSettings _settings = new();
    private bool _settingsWritable = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new WorkspaceStore();
        _store = store;
        store.Paths.EnsureCreated();

        LoadSettings(store);

        // 자동 열기가 켜져 있고 대상이 유효하면 런처를 건너뛴다 (§3.1).
        if (_settings.SkipLauncher && _settings.LastWorkspaceId is { } id)
        {
            var result = store.LoadWorkspace(id);
            if (result.Value is not null)
            {
                OpenWorkspace(result.Value);
                return;
            }

            // 대상이 사라졌거나 손상됐다. 조용히 넘기지 않고 알린 뒤 런처로 떨어진다 (§5).
            MessageBox.Show(
                "자동으로 열 작업 관리를 읽지 못해 목록을 띄운다.\n\n" +
                $"{result.Failure!.Path}\n{result.Failure.Message}",
                "FolderDeck",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ShowLauncher();
    }

    private void ShowLauncher()
    {
        // 워크스페이스가 0개여도 런처는 뜬다 — 여기서 첫 묶음을 만들 수 있어야 하므로.
        var viewModel = new LauncherViewModel(
            _store!,
            _settings,
            OpenWorkspace,
            new FolderPicker(),
            new UserPrompt(),
            new ShellLauncher(),
            _settingsWritable);

        var window = new LauncherWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    /// <summary>런처의 "열기"와 자동 열기가 함께 쓰는 경로.</summary>
    private void OpenWorkspace(Workspace workspace)
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
            new MacroEditor());
        var window = new MainWindow(viewModel);

        // 런처를 닫기 전에 메인 창을 띄운다 — 순서가 바뀌면 창이 0개가 되는 순간에
        // (ShutdownMode.OnLastWindowClose) 앱이 종료된다.
        var launcher = MainWindow as LauncherWindow;
        MainWindow = window;
        window.Show();
        launcher?.Close();
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
        // 밀린 디바운스 저장을 마무리한다 (§5).
        _store?.Dispose();
        base.OnExit(e);
    }
}
