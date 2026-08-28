using System.Diagnostics;
using System.IO.Pipes;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// 워크스페이스당 단일 인스턴스 (§6.2 · Phase 4 Stage 1) — 인자 파싱, 뮤텍스 상태 판정,
/// 신호 helper. UI 밖의 것만 다룬다.
/// </summary>
public sealed class InstanceTests
{
    private static readonly TimeSpan SignalWait = TimeSpan.FromSeconds(5);

    // ── 명령줄 인자 (§3.1) ───────────────────────────────────────────────────

    [Fact]
    public void NoArgumentsMeansFollowTheSettings()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.Null(options.WorkspaceId);
        Assert.False(options.ForceLauncher);
        Assert.Null(options.Error);
    }

    [Fact]
    public void WorkspaceSwitchCarriesTheId()
    {
        var id = Guid.NewGuid();

        var options = CommandLineOptions.Parse(["--workspace", id.ToString()]);

        Assert.Equal(id, options.WorkspaceId);
        Assert.Null(options.Error);
    }

    [Fact]
    public void WorkspaceSwitchAlsoTakesAnEqualsForm()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, CommandLineOptions.Parse([$"--workspace={id}"]).WorkspaceId);
    }

    [Fact]
    public void LauncherSwitchIsRecognised()
    {
        var options = CommandLineOptions.Parse(["--launcher"]);

        Assert.True(options.ForceLauncher);
        Assert.Null(options.WorkspaceId);
        Assert.Null(options.Error);
    }

    [Fact]
    public void SwitchesAreCaseInsensitive()
    {
        Assert.True(CommandLineOptions.Parse(["--Launcher"]).ForceLauncher);
    }

    /// <summary>둘이 같이 오면 워크스페이스가 이긴다 — 무엇을 열지 못박은 쪽이 강하다.</summary>
    [Fact]
    public void AnExplicitWorkspaceBeatsForceLauncher()
    {
        var id = Guid.NewGuid();

        var options = CommandLineOptions.Parse(["--launcher", "--workspace", id.ToString()]);

        Assert.Equal(id, options.WorkspaceId);
        Assert.True(options.ForceLauncher);
        Assert.Null(options.Error);
    }

    [Fact]
    public void AnUnreadableIdIsReportedNotSwallowed()
    {
        var options = CommandLineOptions.Parse(["--workspace", "이건-GUID-가-아니다"]);

        Assert.Null(options.WorkspaceId);
        Assert.NotNull(options.Error);
        Assert.Contains("이건-GUID-가-아니다", options.Error);
    }

    [Fact]
    public void AMissingIdIsReported()
    {
        var options = CommandLineOptions.Parse(["--workspace"]);

        Assert.Null(options.WorkspaceId);
        Assert.NotNull(options.Error);
    }

    [Fact]
    public void UnknownArgumentsAreReported()
    {
        var options = CommandLineOptions.Parse(["--nope"]);

        Assert.NotNull(options.Error);
        Assert.Contains("--nope", options.Error);
    }

    // ── 이름 규칙 (Stage 0 실측) ─────────────────────────────────────────────

    [Fact]
    public void MutexNamesAreSessionScopedAndLowercase()
    {
        var id = Guid.NewGuid();

        var name = InstanceNames.WorkspaceMutex(id);

        Assert.StartsWith(@"Local\FolderDeck.", name);
        Assert.EndsWith(id.ToString(), name);

        // 커널 객체 이름은 대소문자를 구분한다 — 한쪽만 대문자로 만들면 탐지가 무력화된다.
        Assert.Equal(name, $@"Local\FolderDeck.{id.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public void DifferentWorkspacesGetDifferentNames()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.NotEqual(InstanceNames.WorkspaceMutex(a), InstanceNames.WorkspaceMutex(b));
        Assert.NotEqual(InstanceNames.WorkspacePipe(a), InstanceNames.WorkspacePipe(b));
    }

    /// <summary>파이프 이름공간은 머신 전역이라 세션 id 를 이름에 넣어 범위를 뮤텍스와 맞춘다.</summary>
    [Fact]
    public void PipeNamesCarryTheSessionId()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"FolderDeck.s{InstanceNames.SessionId}.{id}", InstanceNames.WorkspacePipe(id));
    }

    // ── 뮤텍스 상태 판정 (§3.2) ──────────────────────────────────────────────

    [Fact]
    public void TheFirstInstanceGetsTheLock()
    {
        var name = UniqueMutexName();

        using var first = InstanceLock.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Equal(name, first.Name);
    }

    /// <summary>
    /// 살아 있는 주인이 있으면 못 잡는다. 뮤텍스 소유권은 스레드에 묶이므로 다른 스레드에서 잡아
    /// 실제 두 프로세스와 같은 상황을 만든다.
    /// </summary>
    [Fact]
    public void ASecondInstanceIsRefusedWhileTheOwnerLives()
    {
        var name = UniqueMutexName();
        using var owned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = new Thread(() =>
        {
            using var held = InstanceLock.TryAcquire(name);
            Assert.NotNull(held);
            owned.Set();
            release.Wait(SignalWait);
        }) { IsBackground = true };

        holder.Start();
        Assert.True(owned.Wait(SignalWait));

        Assert.Null(InstanceLock.TryAcquire(name));

        release.Set();
        Assert.True(holder.Join(SignalWait));

        // 주인이 정상적으로 놓으면 다음 인스턴스가 잡는다.
        using var next = InstanceLock.TryAcquire(name);
        Assert.NotNull(next);
    }

    /// <summary>
    /// <b>이 Stage 의 핵심 분기</b> (§3.2-2). 주인이 정리 없이 사라진 뮤텍스는
    /// <c>AbandonedMutexException</c> 으로 나타나고, 그것은 "소유권이 나에게 넘어왔다"는
    /// <b>성공</b>이다. <c>createdNew</c> 로 판정하면 여기서 틀린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 스레드가 뮤텍스를 놓지 않고 끝나면 커널이 프로세스 강제 종료와 같은 "버려진" 상태를
    /// 만든다 — 프로세스를 죽이지 않고 같은 분기를 정확히 밟을 수 있다.
    /// </para>
    /// <para>
    /// <b>인수는 잠깐 기다려 본다.</b> <see cref="Thread.Join()"/> 가 돌아온 뒤에도 커널이 그
    /// 스레드를 정리하며 소유권을 푸는 데 아주 짧은 시간이 걸려, 바로 재 보면 아직 "주인 있음"이
    /// 나올 수 있다(테스트를 다른 테스트와 같이 돌릴 때 실제로 그랬다). 앱에서는 프로세스가
    /// 죽고 사용자가 다시 실행할 때까지 수백 ms 가 지나므로 이 창이 문제가 되지 않는다.
    /// 여기서 확인하는 것은 "버려진 이름이 <b>영구히</b> 막히지 않는다"다.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbandonedMutexIsTakenOverNotTreatedAsBusy()
    {
        var name = UniqueMutexName();
        using var abandoned = new ManualResetEventSlim();

        var owner = new Thread(() =>
        {
            // 일부러 Dispose/ReleaseMutex 없이 스레드를 끝낸다.
            var held = InstanceLock.TryAcquire(name);
            Assert.NotNull(held);
            abandoned.Set();
        }) { IsBackground = true };

        owner.Start();
        Assert.True(abandoned.Wait(SignalWait));
        Assert.True(owner.Join(SignalWait));

        // AbandonedMutexException 을 잡지 않으면 여기서 그 예외가 그대로 터진다 — 즉 이 테스트가
        // 통과하는 것 자체가 그 catch 경로가 살아 있다는 뜻이다.
        InstanceLock? taken = null;
        var waited = Stopwatch.StartNew();
        while (taken is null && waited.Elapsed < SignalWait)
        {
            taken = InstanceLock.TryAcquire(name);
            if (taken is null)
            {
                Thread.Sleep(10);
            }
        }

        using var acquired = taken;
        Assert.NotNull(acquired);
    }

    // ── 신호 helper (§3.3 · §3.4) ────────────────────────────────────────────

    [Fact]
    public void ASignalReachesTheListener()
    {
        var pipe = UniquePipeName();
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(pipe, arrived.Set);

        Assert.True(listener.IsListening);
        Assert.True(ActivationSignal.TrySend(pipe));
        Assert.True(arrived.Wait(SignalWait));
    }

    /// <summary>같은 창을 두 번, 세 번 눌러도 매번 와야 한다 — 서버를 다시 만들어 계속 기다린다.</summary>
    [Fact]
    public void TheListenerKeepsListeningAfterASignal()
    {
        var pipe = UniquePipeName();
        var count = 0;
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(pipe, () =>
        {
            Interlocked.Increment(ref count);
            arrived.Set();
        });

        for (var i = 1; i <= 3; i++)
        {
            arrived.Reset();
            Assert.True(ActivationSignal.TrySend(pipe), $"{i}번째 신호");
            Assert.True(arrived.Wait(SignalWait), $"{i}번째 수신");
        }

        Assert.Equal(3, Volatile.Read(ref count));
    }

    /// <summary>받을 사람이 없으면(= 창이 없다) 실패로 돌아온다. 부르는 쪽이 그걸로 판단한다.</summary>
    [Fact]
    public void SendingToNobodyFails()
    {
        Assert.False(ActivationSignal.TrySend(UniquePipeName()));
    }

    /// <summary>
    /// 좀비 모사 ① — 파이프 <b>이름은 있는데</b> 붙을 자리가 없다(유일한 인스턴스를 남이 물고
    /// 있다). 실패로 돌아와야 한다 (§3.2-3 이 그 실패 위에 서 있다).
    /// </summary>
    [Fact]
    public async Task SendingToAPipeWithNoFreeInstanceFails()
    {
        var pipe = UniquePipeName();
        using var server = new NamedPipeServerStream(
            pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();

        using var hog = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
        hog.Connect(2000);
        await accept.WaitAsync(SignalWait);

        Assert.False(ActivationSignal.TrySend(pipe));
    }

    /// <summary>
    /// 좀비 모사 ② — 붙기는 하는데 상대가 <b>읽지 않는다</b>. 실측된 함정이다: 이 경우 파이프
    /// 쓰기가 무한히 막힐 수 있고, 그러면 워크스페이스가 아예 안 열린다. 무슨 답이 나오든
    /// <b>돌아오기는 해야</b> 한다.
    /// </summary>
    [Fact]
    public async Task SendingToAServerThatNeverReadsStillReturns()
    {
        var pipe = UniquePipeName();
        using var server = new NamedPipeServerStream(
            pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();

        var elapsed = Stopwatch.StartNew();
        ActivationSignal.TrySend(pipe);
        elapsed.Stop();

        await accept.WaitAsync(SignalWait);

        // 성공/실패는 커널 버퍼링에 달려 있어 단정하지 않는다. 한도 안에 돌아오는 것이 요점이다.
        Assert.True(elapsed.Elapsed < SignalWait, $"{elapsed.Elapsed.TotalSeconds:F1}초 걸렸다");
    }

    /// <summary>같은 이름을 두 번 열 수 없다 — 그 사실이 조용히 묻히지 않고 Failure 로 올라온다.</summary>
    [Fact]
    public void ASecondListenerOnTheSameNameReportsWhyItCannotListen()
    {
        var pipe = UniquePipeName();
        using var first = new ActivationListener(pipe, () => { });
        Assert.True(first.IsListening);

        using var second = new ActivationListener(pipe, () => { });

        Assert.False(second.IsListening);
        Assert.NotNull(second.Failure);
    }

    /// <summary>
    /// 본문이 맞을 때만 창을 올린다. 남이 존재 확인만 한 연결(본문 없음)에 창이 튀어나오면
    /// 안 되기 때문이다 — <c>File.Exists</c> 가 실제로 그런 연결을 만든다(실측).
    /// </summary>
    [Fact]
    public void AnEmptyConnectionDoesNotActivate()
    {
        var pipe = UniquePipeName();
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(pipe, arrived.Set);

        // 연결만 하고 아무 것도 쓰지 않고 끊는다.
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
        {
            client.Connect(2000);
        }

        Assert.False(arrived.Wait(TimeSpan.FromMilliseconds(400)));

        // 그리고 진짜 신호는 여전히 통한다 — 서버가 다시 열리는 데 걸리는 시간은 이 판이
        // 무는 성질이 아니라 OS 스케줄링에 건 마진일 뿐이므로(D62), 한 번만 쏘지 않고
        // 통할 때까지 되쏜다.
        Assert.True(TrySendUntilItWorks(pipe, SignalWait));
        Assert.True(arrived.Wait(SignalWait));
    }

    /// <summary>
    /// D62 — 「빈 연결 뒤에도 신호가 결국 통한다」를 무는 것이지 「몇 ms 안에 통한다」를 무는
    /// 것이 아니다. <paramref name="budget"/> 동안 짧은 간격으로 되쏜다.
    /// </summary>
    private static bool TrySendUntilItWorks(string pipe, TimeSpan budget)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < budget)
        {
            if (ActivationSignal.TrySend(pipe))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    // ── 배지 판정 (§4.2) ─────────────────────────────────────────────────────

    [Fact]
    public void OpenWorkspacesAreTheOnesThatCanReceiveASignal()
    {
        var open = Guid.NewGuid();
        var closed = Guid.NewGuid();
        var signals = new InstanceSignals();

        using var listener = new ActivationListener(InstanceNames.WorkspacePipe(open), () => { });
        Assert.True(listener.IsListening);

        var result = signals.WhichAreOpen([open, closed]);

        Assert.Contains(open, result);
        Assert.DoesNotContain(closed, result);
    }

    /// <summary>
    /// 배지를 재는 것이 신호 경로를 망가뜨리지 않아야 한다. <c>File.Exists</c> 로 재면
    /// 대기 중인 연결을 잡아먹어 <b>바로 다음 신호가 실패한다</b>(실측) — 그래서 이름공간 열거로 잰다.
    /// </summary>
    [Fact]
    public void CheckingTheBadgeDoesNotConsumeThePendingConnection()
    {
        var id = Guid.NewGuid();
        var signals = new InstanceSignals();
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(InstanceNames.WorkspacePipe(id), arrived.Set);

        for (var i = 0; i < 5; i++)
        {
            Assert.Contains(id, signals.WhichAreOpen([id]));
        }

        Assert.True(signals.TrySendActivate(id));
        Assert.True(arrived.Wait(SignalWait));
    }

    [Fact]
    public void NoCandidatesMeansNoLookup()
    {
        Assert.Empty(new InstanceSignals().WhichAreOpen([]));
    }

    // ── 존재 확인 (배지 판정 수단) ───────────────────────────────────────────

    [Fact]
    public void IsLiveSeesAListenerAndDoesNotSeeAMissingOne()
    {
        var pipe = UniquePipeName();

        Assert.False(InstanceSignals.IsLive(pipe));

        using var listener = new ActivationListener(pipe, () => { });
        Assert.True(listener.IsListening);

        Assert.True(InstanceSignals.IsLive(pipe));
    }

    /// <summary>
    /// 존재 확인이 대기 중인 연결을 잡아먹지 않는다 — 종료 직전에 부르는 자리라
    /// 여기서 남의 신호를 삼키면 안 된다 (<c>File.Exists</c> 는 실제로 삼킨다).
    /// </summary>
    [Fact]
    public void IsLiveDoesNotConsumeThePendingConnection()
    {
        var pipe = UniquePipeName();
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(pipe, arrived.Set);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(InstanceSignals.IsLive(pipe));
        }

        Assert.True(ActivationSignal.TrySend(pipe));
        Assert.True(arrived.Wait(SignalWait));
    }

    private static string UniqueMutexName() =>
        $@"Local\FolderDeck.tests.{Guid.NewGuid():N}";

    private static string UniquePipeName() =>
        $"FolderDeck.tests.s{InstanceNames.SessionId}.{Guid.NewGuid():N}";
}
