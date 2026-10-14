using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace FolderDeck.App.Services;

/// <summary>
/// 인스턴스 신호를 받는 쪽 (§3.3). 파이프 서버를 띄우고, 본문에 따라 콜백을 부른다 —
/// <c>"activate"</c> 면 창을 앞으로 가져온다. 그 밖의 본문에는 아무것도 하지 않는다.
/// </summary>
/// <remarks>
/// <para>
/// <b>콜백은 UI 스레드가 아닌 곳에서 불린다.</b> 받는 쪽에서 UI 스레드로 마샬해야 한다.
/// </para>
/// <para>
/// 연결 하나를 처리하면 서버를 다시 만들어 계속 기다린다 — 같은 워크스페이스를 세 번, 네 번
/// 눌러도 매번 앞으로 와야 하므로.
/// </para>
/// </remarks>
public sealed class ActivationListener : IDisposable
{
    /// <summary>
    /// 붙기만 하고 아무것도 안 쓰는 상대를 기다리는 한도. 이걸 안 두면 그런 연결 하나가
    /// 이후의 모든 신호를 막는다.
    /// </summary>
    private const int ReadTimeoutMs = 2000;

    private readonly string _pipeName;
    private readonly Action<string> _onPayload;
    private readonly IReadOnlySet<string> _accepted;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();

    private NamedPipeServerStream? _server;
    private bool _disposed;

    /// <param name="onActivate">"앞으로" 신호를 받았을 때.</param>
    public ActivationListener(string pipeName, Action onActivate)
        : this(pipeName, _ => onActivate(), [ActivationSignal.ActivatePayload])
    {
        ArgumentNullException.ThrowIfNull(onActivate);
    }

    /// <summary>
    /// 본문 여럿을 가르는 수신자 (2026-10-14 2차 — 상주의 「1회용 창 열기」·「끝내기」). <paramref name="accepted"/>
    /// 밖의 본문(존재 확인만 한 빈 연결 포함)에는 아무것도 안 한다 — 한 가지 본문만 받던 때와 같은 규칙이다.
    /// </summary>
    public ActivationListener(string pipeName, Action<string> onPayload, IReadOnlyCollection<string> accepted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onPayload);
        ArgumentNullException.ThrowIfNull(accepted);

        _pipeName = pipeName;
        _onPayload = onPayload;
        _accepted = new HashSet<string>(accepted, StringComparer.Ordinal);

        // 첫 서버는 여기서 동기로 만든다 — 이름이 이미 쓰이고 있으면(다른 세션 등) 그 사실을
        // 창을 띄우기 전에 알 수 있어야 한다. 조용히 "신호를 못 받는 창"이 되지 않게 (§5).
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

    /// <summary>파이프 서버를 띄우지 못한 이유. 정상이면 null.</summary>
    public string? Failure { get; }

    /// <summary>신호를 받을 수 있는 상태인가.</summary>
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

                // 본문이 맞을 때만 반응한다. 남이 존재 확인만 한 연결(본문 없음)에는 아무것도 안 한다.
                if (line is not null && _accepted.Contains(line))
                {
                    _onPayload(line);
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose 또는 읽기 한도. 한도면 다음 연결을 기다린다.
            }
            catch (IOException)
            {
                // 상대가 중간에 끊었다. 다음 연결을 기다린다.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // 콜백이 디스패처에 넣지 못했다(종료 중). 더 기다릴 이유가 없다.
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
