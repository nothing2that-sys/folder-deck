using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace FolderDeck.App.Services;

public sealed class ActivationListener : IDisposable
{

    private const int ReadTimeoutMs = 2000;

    private readonly string _pipeName;
    private readonly Action _onActivate;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();

    private NamedPipeServerStream? _server;
    private bool _disposed;

    public ActivationListener(string pipeName, Action onActivate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onActivate);

        _pipeName = pipeName;
        _onActivate = onActivate;

        try
        {
            _server = CreateServer();
        }
        catch (IOException ex)
        {
            Failure = ex.Message;
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Failure = ex.Message;
            return;
        }

        _ = Task.Run(ListenAsync);
    }

    public string? Failure { get; }

    public bool IsListening => Failure is null;

    private NamedPipeServerStream CreateServer() => new(
        _pipeName,
        PipeDirection.In,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var server = Current;
            if (server is null)
            {
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                readCts.CancelAfter(ReadTimeoutMs);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);

                if (line == ActivationSignal.ActivatePayload)
                {
                    _onActivate();
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (IOException)
            {

            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {

                return;
            }
            finally
            {
                server.Dispose();
            }

            if (_cts.IsCancellationRequested || !TryRenew())
            {
                return;
            }
        }
    }

    private NamedPipeServerStream? Current
    {
        get
        {
            lock (_gate)
            {
                return _disposed ? null : _server;
            }
        }
    }

    private bool TryRenew()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                _server = CreateServer();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        NamedPipeServerStream? server;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            server = _server;
            _server = null;
        }

        _cts.Cancel();
        server?.Dispose();
        _cts.Dispose();
    }
}
