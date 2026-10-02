using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FolderDeck.App.Services;

public static class ActivationSignal
{

    public const string ActivatePayload = "activate";

    private const int ConnectTimeoutMs = 500;

    private const int SignalTimeoutMs = 1500;

    private const int AbandonAfterMs = 3000;

    private const uint AsfwAny = 0xFFFFFFFF;

    private static readonly byte[] ActivateBytes = Encoding.UTF8.GetBytes(ActivatePayload + "\n");

    public static bool TrySend(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        AllowSetForegroundWindow(AsfwAny);

        return Send(pipeName, ActivateBytes);
    }

    private static bool Send(string pipeName, byte[] payload)
    {
        try
        {
            var send = Task.Run(() => SendAsync(pipeName, payload));
            return send.Wait(AbandonAfterMs) && send.Result;
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    private static async Task<bool> SendAsync(string pipeName, byte[] payload)
    {
        try
        {
            using var cts = new CancellationTokenSource(SignalTimeoutMs);

            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            await client.ConnectAsync(ConnectTimeoutMs, cts.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, cts.Token).ConfigureAwait(false);
            await client.FlushAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {

            return false;
        }
        catch (OperationCanceledException)
        {

            return false;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
