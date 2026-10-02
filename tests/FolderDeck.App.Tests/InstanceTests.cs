using System.Diagnostics;
using System.IO.Pipes;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

public sealed class InstanceTests
{
    private static readonly TimeSpan SignalWait = TimeSpan.FromSeconds(5);

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

    [Fact]
    public void MutexNamesAreSessionScopedAndLowercase()
    {
        var id = Guid.NewGuid();

        var name = InstanceNames.WorkspaceMutex(id);

        Assert.StartsWith(@"Local\FolderDeck.", name);
        Assert.EndsWith(id.ToString(), name);

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

    [Fact]
    public void PipeNamesCarryTheSessionId()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"FolderDeck.s{InstanceNames.SessionId}.{id}", InstanceNames.WorkspacePipe(id));
    }

    [Fact]
    public void TheFirstInstanceGetsTheLock()
    {
        var name = UniqueMutexName();

        using var first = InstanceLock.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Equal(name, first.Name);
    }

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

        using var next = InstanceLock.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public void AnAbandonedMutexIsTakenOverNotTreatedAsBusy()
    {
        var name = UniqueMutexName();
        using var abandoned = new ManualResetEventSlim();

        var owner = new Thread(() =>
        {

            var held = InstanceLock.TryAcquire(name);
            Assert.NotNull(held);
            abandoned.Set();
        }) { IsBackground = true };

        owner.Start();
        Assert.True(abandoned.Wait(SignalWait));
        Assert.True(owner.Join(SignalWait));

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

    [Fact]
    public void SendingToNobodyFails()
    {
        Assert.False(ActivationSignal.TrySend(UniquePipeName()));
    }

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

        Assert.True(elapsed.Elapsed < SignalWait, $"{elapsed.Elapsed.TotalSeconds:F1}초 걸렸다");
    }

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

    [Fact]
    public void AnEmptyConnectionDoesNotActivate()
    {
        var pipe = UniquePipeName();
        using var arrived = new ManualResetEventSlim();
        using var listener = new ActivationListener(pipe, arrived.Set);

        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
        {
            client.Connect(2000);
        }

        Assert.False(arrived.Wait(TimeSpan.FromMilliseconds(400)));

        Assert.True(TrySendUntilItWorks(pipe, SignalWait));
        Assert.True(arrived.Wait(SignalWait));
    }

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

    [Fact]
    public void IsLiveSeesAListenerAndDoesNotSeeAMissingOne()
    {
        var pipe = UniquePipeName();

        Assert.False(InstanceSignals.IsLive(pipe));

        using var listener = new ActivationListener(pipe, () => { });
        Assert.True(listener.IsListening);

        Assert.True(InstanceSignals.IsLive(pipe));
    }

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
