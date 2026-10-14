using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;
using Forms = System.Windows.Forms;

namespace FolderDeck.App.Services;

/// <summary>
/// 상주 프로그램 (2026-10-14 2차 · JH 결정 B안). 트레이에 남아 1회용 창 단축키를 직접 받고(<c>RegisterHotKey</c>),
/// 단축키·시작 메뉴 신호가 오면 <b>이 프로세스 안에서</b> 1회용 창을 연다 — 프로세스 시작 비용이 없다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>세션에 하나(<see cref="InstanceNames.AgentMutex"/>). 이미 있으면 <see cref="TryStart"/> 가 false.</item>
/// <item>마지막 1회용 창을 닫아도 남는다 — <see cref="ShutdownMode.OnExplicitShutdown"/>.</item>
/// <item>설정은 <c>settings.json</c> 을 감시해 다시 읽고 <b>같은 인스턴스에 덮어쓴다</b>(<see cref="AppSettings.CopyFrom"/>) —
///   열린 1회용 창들이 같은 인스턴스를 들고 있고, 상주가 오래 들고 있는 낡은 값을 나중에 저장하면 다른 창의 변경을 되돌린다.</item>
/// <item><see cref="AppSettings.UseResidentAgent"/> 가 꺼지면 <b>물러난다</b>(<see cref="Retire"/>) — 단축키·트레이·신호·뮤텍스를
///   곧바로 내려놓고, 열린 1회용 창이 다 닫히면 프로세스가 끝난다(JH 결정 — 지금 쓰던 창이 갑자기 닫히지 않게).
///   그동안 단축키는 바로가기가 받고, 다시 켜면 새 상주가 뜰 수 있다(뮤텍스를 이미 놓았으므로).</item>
/// <item>시작하자마자 1회용 창 하나를 화면 밖에서 만들었다 닫는다(<see cref="ScratchWindowHost.WarmUp"/>) — 첫 단축키도
///   두 번째부터와 같은 속도로 뜨게.</item>
/// </list>
/// </remarks>
public sealed class AgentRuntime : IDisposable
{
    private readonly Application _app;
    private readonly IWorkspaceStore _store;
    private readonly AppSettings _settings;
    private readonly ScratchWindowHost _host;
    private readonly ISelfLauncher _selfLauncher;
    private readonly IHotkeyRouting _routing;

    private System.Threading.Mutex? _mutex;
    private ActivationListener? _listener;
    private GlobalHotkey? _hotkey;
    private AgentHotkeyBinder? _binder;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _openItem;
    private FileSystemWatcher? _settingsWatcher;
    private DispatcherTimer? _settingsTimer;
    private bool _exiting;
    private bool _retired;

    /// <summary>등록 실패 뒤 다시 시도하는 타이머와 남은 횟수 — 탐색기가 막 놓은 키를 곧바로 못 잡는 경우가 있다.</summary>
    private DispatcherTimer? _retryTimer;
    private int _retriesLeft;
    private const int RegisterRetries = 6;
    private static readonly TimeSpan RegisterRetryInterval = TimeSpan.FromMilliseconds(500);

    public AgentRuntime(
        Application app, IWorkspaceStore store, AppSettings settings, ScratchWindowHost host, ISelfLauncher selfLauncher,
        IHotkeyRouting routing)
    {
        _app = app;
        _store = store;
        _settings = settings;
        _host = host;
        _selfLauncher = selfLauncher;
        _routing = routing;
    }

    /// <summary>
    /// 상주를 세운다. 이미 다른 상주가 있거나 설정이 상주를 끄라고 하면 아무것도 안 하고 false — 부르는 쪽이
    /// 프로세스를 끝낸다.
    /// </summary>
    public bool TryStart()
    {
        if (!_settings.UseResidentAgent)
        {
            return false;
        }

        var mutex = new System.Threading.Mutex(initiallyOwned: false, InstanceNames.AgentMutex);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _listener = new ActivationListener(
            InstanceNames.AgentPipe,
            payload => _app.Dispatcher.BeginInvoke(() => OnPayload(payload)),
            [AgentPayloads.Scratch, AgentPayloads.Exit]);

        _hotkey = new GlobalHotkey(OpenScratch);
        _binder = new AgentHotkeyBinder(_hotkey);

        CreateTray();
        ApplyHotkey();
        WatchSettings();

        // 데우기 — 트레이가 먼저 뜨게 한가할 때 한다.
        _app.Dispatcher.BeginInvoke(() => _host.WarmUp(), DispatcherPriority.ApplicationIdle);
        return true;
    }

    /// <summary>1회용 창 하나를 연다(단축키 · 트레이 · 시작 메뉴 신호 공용).</summary>
    public void OpenScratch()
    {
        if (!_exiting && !_retired)
        {
            _host.Open();
        }
    }

    /// <summary>
    /// 「상주로 받기」가 꺼졌다 — 단축키·트레이·신호·설정 감시·뮤텍스를 곧바로 내려놓고, 열린 1회용 창이 다 닫히면
    /// 프로세스를 끝낸다. 창이 없으면 바로 끝낸다.
    /// </summary>
    public void Retire()
    {
        if (_exiting || _retired)
        {
            return;
        }

        _retired = true;

        // 순서가 핵심이다: 단축키를 <b>먼저 놓고</b>, 그다음 바로가기에 키를 쓴다. 바로가기가 먼저 바뀌면 탐색기가 그 키를
        // 등록하려다 상주에 막혀 실패하고, 다시 시도하지 않아 단축키가 아예 죽는다(JH 손확인).
        _retryTimer?.Stop();
        _binder?.Clear();
        if (_routing.Apply(_settings) is { } failure)
        {
            Notify($"바로가기 단축키를 맞추지 못했다 — {failure}", Forms.ToolTipIcon.Warning);
        }

        Dispose();

        if (_host.OpenWindows.Count == 0)
        {
            Exit();
            return;
        }

        _host.LastWindowClosed += (_, _) => Exit();
    }

    /// <summary>열린 1회용 창을 닫고 트레이·단축키·신호를 치우고 프로세스를 끝낸다.</summary>
    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        // 「끔」으로 물러난 경우(Retire)는 이미 바로가기를 맞췄다. 그 밖(트레이 「상주 끝내기」 · --agent-exit)은 상주가 없는
        // 동안에도 단축키가 먹게 바로가기에 넘겨준다 — 단축키를 먼저 놓고 쓴다(JH 결정 · 순서는 Retire 와 같은 이유).
        if (!_retired)
        {
            _retryTimer?.Stop();
            _binder?.Clear();
            _routing.HandOverToShortcut(_settings);
        }

        _host.CloseAll();
        Dispose();
        _app.Shutdown();
    }

    public void Dispose()
    {
        _retryTimer?.Stop();
        StopWatchingSettings();
        _listener?.Dispose();
        _listener = null;
        _binder?.Clear();
        _hotkey?.Dispose();
        _hotkey = null;

        if (_tray is { } tray)
        {
            tray.Visible = false;
            tray.Dispose();
            _tray = null;
        }

        if (_mutex is { } mutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 이 스레드가 소유하지 않았다 — 이미 놓였다.
            }

            mutex.Dispose();
            _mutex = null;
        }
    }

    // ── 신호 · 단축키 ─────────────────────────────────────────────────────────

    private void OnPayload(string payload)
    {
        switch (payload)
        {
            case AgentPayloads.Scratch:
                OpenScratch();
                break;
            case AgentPayloads.Exit:
                Exit();
                break;
            default:
                break;
        }
    }

    private void ApplyHotkey()
    {
        _retryTimer?.Stop();
        _retriesLeft = RegisterRetries;
        TryRegisterHotkey();
    }

    /// <summary>
    /// 등록을 시도한다. 실패하면 잠깐씩 몇 번 더 시도한 뒤에야 알린다 — 「상주로 받기」를 막 켰을 때는 탐색기가 바로가기
    /// 키를 아직 놓지 않았을 수 있다(바로가기 키를 지운 직후 상주가 뜬다).
    /// </summary>
    private void TryRegisterHotkey()
    {
        if (_binder is null || _exiting || _retired)
        {
            return;
        }

        var failure = _binder.Update(_settings.ScratchHotkey);
        if (_openItem is not null)
        {
            _openItem.Text = _settings.ScratchHotkey.Length == 0
                ? "1회용 창 열기"
                : $"1회용 창 열기 ({_settings.ScratchHotkey})";
        }

        if (failure is null)
        {
            return;
        }

        if (_retriesLeft-- > 0)
        {
            _retryTimer ??= CreateRetryTimer();
            _retryTimer.Start();
            return;
        }

        Notify($"단축키 {_settings.ScratchHotkey} 을(를) 쓸 수 없다 — {failure}", Forms.ToolTipIcon.Warning);
    }

    private DispatcherTimer CreateRetryTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _app.Dispatcher) { Interval = RegisterRetryInterval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryRegisterHotkey();
        };
        return timer;
    }

    // ── 트레이 ────────────────────────────────────────────────────────────────

    private void CreateTray()
    {
        _openItem = new Forms.ToolStripMenuItem("1회용 창 열기", null, (_, _) => OpenScratch());
        var launcherItem = new Forms.ToolStripMenuItem("작업 관리 열기", null, (_, _) => OpenLauncher());
        var exitItem = new Forms.ToolStripMenuItem("상주 끝내기", null, (_, _) => Exit());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_openItem);
        menu.Items.Add(launcherItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new Forms.NotifyIcon
        {
            Text = "FolderDeck — 1회용 창 단축키 대기 중",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenScratch();
    }

    private void OpenLauncher()
    {
        if (_selfLauncher.OpenLauncher() is { } error)
        {
            Notify($"작업 관리 목록을 띄우지 못했다 — {error}", Forms.ToolTipIcon.Warning);
        }
    }

    private void Notify(string text, Forms.ToolTipIcon icon) =>
        _tray?.ShowBalloonTip(5000, "FolderDeck", text, icon);

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
            {
                return icon;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // 아래 기본 아이콘으로.
        }

        return System.Drawing.SystemIcons.Application;
    }

    // ── 설정 감시 ─────────────────────────────────────────────────────────────

    private void WatchSettings()
    {
        var file = _store.Paths.SettingsFile;
        var directory = Path.GetDirectoryName(file);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        _settingsTimer = new DispatcherTimer(DispatcherPriority.Normal, _app.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _settingsTimer.Tick += OnSettingsDue;

        try
        {
            _settingsWatcher = new FileSystemWatcher(directory, Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _settingsWatcher.Changed += OnSettingsFileEvent;
            _settingsWatcher.Created += OnSettingsFileEvent;
            _settingsWatcher.Renamed += OnSettingsFileEvent;
            _settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _settingsWatcher?.Dispose();
            _settingsWatcher = null;
            Notify($"설정 파일을 지켜볼 수 없다 — 단축키를 바꾸면 상주를 다시 시작해야 한다. ({ex.Message})",
                Forms.ToolTipIcon.Warning);
        }
    }

    private void OnSettingsFileEvent(object sender, FileSystemEventArgs e) =>
        _app.Dispatcher.BeginInvoke(() =>
        {
            _settingsTimer?.Stop();
            _settingsTimer?.Start();
        });

    private void OnSettingsDue(object? sender, EventArgs e)
    {
        _settingsTimer?.Stop();

        // 다른 프로세스가 아직 쓰는 중이면 손상으로 읽힐 수 있다 — 그때는 이번 알림을 버린다(다음 쓰기가 다시 알린다).
        if (_store.LoadSettings().Value is not { } loaded)
        {
            return;
        }

        _settings.CopyFrom(loaded);

        if (!_settings.UseResidentAgent)
        {
            Retire();
            return;
        }

        ApplyHotkey();
    }

    private void StopWatchingSettings()
    {
        if (_settingsWatcher is { } watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _settingsWatcher = null;
        }

        if (_settingsTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnSettingsDue;
            _settingsTimer = null;
        }
    }
}
