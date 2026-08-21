using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Watching;

namespace FolderDeck.App.Watching;

/// <summary>
/// 타일 하나가 보고 있는 폴더를 감시한다 (§8 · Phase 5) — 바깥에서 바뀌면 목록이 스스로 갱신된다.
/// </summary>
/// <remarks>
/// <b>기존 갱신 경로(F5 · 창 활성화 · 조작 후)를 대체하지 않는다.</b> 네 번째 자리로 들어간다.
/// <para>
/// <see cref="FileSystemWatcher"/> 의 <c>NotifyFilter</c>·<c>InternalBufferSize</c>·
/// <c>IncludeSubdirectories</c>·<c>Filter</c> 는 <b>넷 다 기본값 그대로 둔다</b>(대입문이
/// 하나도 없는 것이 의도다). 기본값이 이미 우리가 원하는 값이고, 특히 버퍼를 키우는 것은
/// 「느린 소비자」를 고치지 못한다 — 실측에서 버퍼 초과는 건수가 아니라 핸들러가 붙잡은
/// 시간이 갈랐다(즉시 반환이면 32,000건에도 <c>Error</c> 0건).
/// </para>
/// </remarks>
public sealed class FolderWatcher : IDisposable
{
    private readonly FolderPanelViewModel _panel;
    private readonly Dispatcher _dispatcher;
    private readonly ChangeCoalescer _coalescer;

    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>되살리기 시도가 배경에서 도는 중. <b>UI 스레드에서만</b> 읽고 쓴다.</summary>
    private bool _reviving;

    /// <param name="isOperationRunning">
    /// 앱 자신의 파일 조작이 도는 중인가 (<c>MainViewModel.IsOperationRunning</c>). 켜져 있는
    /// 동안 감시 발화를 삼킨다 — <b>새 상태가 아니라 이미 있는 그 플래그를 읽는다</b>.
    /// </param>
    public FolderWatcher(
        FolderPanelViewModel panel, Dispatcher dispatcher, Func<bool> isOperationRunning)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isOperationRunning);

        _panel = panel;
        _dispatcher = dispatcher;

        // 삼킴을 합치기 부품 안에 두는 이유는 <b>자리</b>다. 거기가 Due 를 올리기 직전,
        // 즉 이 클래스가 BeginInvoke 를 부르기 전이라 삼킨 발화는 디스패처를 건드리지도
        // 않는다. 조작이 끝나면 조작 후 갱신(RunGuardedAsync 의 finally)이 전량 재조회로
        // 한 번에 갚으므로 삼킨 것을 따로 들고 있을 필요가 없다.
        _coalescer = new ChangeCoalescer(suppressed: isOperationRunning);
        _coalescer.Due += OnDue;

        _panel.PropertyChanged += OnPanelPropertyChanged;
        _panel.LoadCompleted += OnPanelLoadCompleted;
        Retarget();
    }

    /// <summary>
    /// 감시를 걸지 못하고 삼킨 횟수. 경로가 없거나 접근 불가면 조용히 안 건다(상태 바에
    /// 알리지 않는 것이 v1 결정이다) — F5 와 창 활성화 재조회가 안전망으로 남아 있다.
    /// </summary>
    public int SkippedCount { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _panel.PropertyChanged -= OnPanelPropertyChanged;
        _panel.LoadCompleted -= OnPanelLoadCompleted;
        _coalescer.Due -= OnDue;
        _coalescer.Dispose();
        Stop();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderPanelViewModel.CurrentPath))
        {
            Retarget();
        }
    }

    /// <summary>
    /// 목록 읽기가 끝났다 — <b>갱신하려는 것이 아니다.</b> 저쪽이 그 경로를 방금 읽어 봤다는
    /// 뜻이니, 여기서는 내가 죽어 있는지만 다시 본다.
    /// </summary>
    private void OnPanelLoadCompleted(object? sender, EventArgs e) => TryRevive();

    /// <summary>
    /// <b>봐야 하는데 안 보고 있으면</b> 배경에서 다시 등록한다 (§8 · D37).
    /// </summary>
    /// <remarks>
    /// 「봐야 하는데 안 보고 있다」에 새 플래그를 두지 않는다 — <c>_watcher is null</c> 과
    /// <c>CurrentPath is not null</c> 두 값으로 유도된다. 진실이 두 곳이 되면 하나만 켜지는
    /// 사고가 난다.
    /// <para>
    /// 끊긴 네트워크 경로에서 <see cref="FileSystemWatcher"/> 를 <b>만드는 일</b>은 오래
    /// 붙잡을 수 있다(워크스페이스 열기에서 약 30초 실측). 그 한 가지만 배경으로 밀어낸다 —
    /// 폴더 열거가 이미 같은 이유로 나가 있다. <b><see cref="_watcher"/> 필드에 쓰는 것은 UI
    /// 스레드뿐</b>이다.
    /// </para>
    /// <para>
    /// 시도 하나가 오래 걸리므로 <b>「이미 시도 중이면 새로 시작하지 않는다」 하나로</b> 반복
    /// 비용이 막힌다. 주기 점검도, 시간 기반 유예도 두지 않는다.
    /// </para>
    /// </remarks>
    private void TryRevive()
    {
        if (_disposed)
        {
            return;
        }

        // 살아 있다. 평상시엔 여기서 끝난다 — 목록 읽기마다 불리지만 비용이 0인 자리다.
        if (_watcher is not null)
        {
            return;
        }

        // 볼 것이 없다 — 빈 순환 칸이다.
        if (_panel.CurrentPath is not { } path)
        {
            return;
        }

        if (_reviving)
        {
            return;
        }

        _reviving = true;
        _ = Task.Run(() => Revive(path));
    }

    /// <summary>
    /// 배경 스레드. <b>여기서 하는 일은 새 인스턴스를 만드는 것뿐</b>이고
    /// <see cref="_watcher"/> 는 건드리지 않는다 — 마무리는 UI 스레드가 한다.
    /// </summary>
    private void Revive(string path)
    {
        FileSystemWatcher? made = null;

        try
        {
            made = new FileSystemWatcher(path);
            made.Created += OnChanged;
            made.Changed += OnChanged;
            made.Deleted += OnChanged;
            made.Renamed += OnRenamed;
            made.Error += OnError;
            made.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 아직 닿지 못한다. 다음 목록 읽기가 또 물어볼 것이다.
            if (made is not null)
            {
                Discard(made);
                made = null;
            }
        }

        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            if (made is not null)
            {
                Discard(made);
            }

            return;
        }

        var revived = made;
        _dispatcher.BeginInvoke(() => FinishRevive(path, revived));
    }

    /// <summary>
    /// UI 스레드. 만드는 동안 판이 바뀌었으면 <b>만든 것을 버린다</b> — 뒤늦은 등록이
    /// 지금 보고 있는 폴더를 덮어쓰지 않게.
    /// </summary>
    private void FinishRevive(string path, FileSystemWatcher? made)
    {
        _reviving = false;

        if (made is null)
        {
            // 되살리기의 실패는 여기서만 센다 — 세는 자리가 둘이 되면 값의 주인이 없어진다.
            SkippedCount++;
            return;
        }

        // 버려야 하는 세 경우: 그 사이 Dispose 됐다 · Retarget 이 먼저 성공했다 · 이동했다.
        if (_disposed
            || _watcher is not null
            || _panel.CurrentPath is not { } current
            || !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            Discard(made);
            return;
        }

        // 갱신을 부르지 않는다 — 이 신호를 낸 것이 방금 끝난 목록 읽기라 이미 최신이다.
        _watcher = made;
    }

    /// <summary>
    /// 감시 대상을 지금 보고 있는 경로로 갈아끼운다.
    /// <b>대기 중인 다발을 먼저 접는다</b> — 옛 경로가 부른 갱신이 새 경로에서 헛돌지 않게.
    /// </summary>
    private void Retarget()
    {
        _coalescer.Cancel();

        var path = _panel.CurrentPath;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        if (path is null)
        {
            Stop();
            return;
        }

        try
        {
            if (_watcher is null)
            {
                _watcher = new FileSystemWatcher(path);
                _watcher.Created += OnChanged;
                _watcher.Changed += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Error += OnError;
            }
            else
            {
                _watcher.Path = path;
            }

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 경로가 없거나 닿지 못한다. 앱을 세우지 않는다.
            SkippedCount++;
            Stop();
        }
    }

    private void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        Discard(_watcher);
        _watcher = null;
    }

    /// <summary>이 감시자를 버린다 — 뗀 뒤에 버리는 순서를 한 자리에 둔다.</summary>
    private void Discard(FileSystemWatcher watcher)
    {
        watcher.Created -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;

        watcher.Dispose();
    }

    // ── 핸들러는 무조건 즉시 반환한다 ────────────────────────────────────────
    //
    // 이 구현의 제1 제약이다. 여기서 파일을 만지거나 갱신을 부르거나 로그를 쓰면
    // 감시자의 내부 버퍼가 밀려 이벤트를 삼킨다(실측: 5ms 만 붙잡아도 1,000건에
    // 로컬 4건 · 원격 9건의 Error 가 났고, 즉시 반환이면 32,000건에도 0건이었다).

    private void OnChanged(object sender, FileSystemEventArgs e) => _coalescer.Signal();

    private void OnRenamed(object sender, RenamedEventArgs e) => _coalescer.Signal();

    /// <summary>
    /// 버퍼 초과. <b>감시자를 다시 만들지 않는다</b> — 실측에서 <c>Error</c> 뒤에도 감시자는
    /// 계속 돌았다(로컬·원격 모두). 몇 건을 삼켰는지는 알 방법이 없으므로 전량 재조회로 갚는다.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e) => _coalescer.SignalNow();

    private void OnDue(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>UI 스레드. 여기서부터는 타일 하나의 갱신이다.</summary>
    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        // ── E-4 임시 계측. 이 줄이 menuDepth=1 로 찍히면 감시 갱신이 중첩 메시지 루프
        // 안에서 실행된 것이다 — 조사 §4 중점 3 이 바로 그 갈림이다.
        ShellMenuProbe.Log("Watcher", $"Refresh {ShellMenuProbe.MenuContext()}");

        // 재귀 검색이 걸려 있으면 삼키고 잊는다. 갱신은 Items 를 갈아끼우고, 그 통지가
        // 검색을 처음부터 다시 돌린다 — 바깥에서 파일 하나만 바뀌어도 검색이 리셋된다.
        // 쌓아 두었다가 나중에 터뜨리지도 않는다.
        if (_panel.IsRecursiveSearch)
        {
            return;
        }

        // 그 타일의 폴더 하나만 바뀌었다. RefreshAllAsync 가 아니다.
        // 갱신이 MaxWait 보다 오래 걸려도 괜찮다 — 겹침 방어와 최신성 확인이 이미 있다.
        _ = _panel.RefreshAsync();
    }
}
