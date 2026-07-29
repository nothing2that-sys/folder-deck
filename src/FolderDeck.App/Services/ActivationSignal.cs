using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FolderDeck.App.Services;

/// <summary>
/// "그 창을 앞으로" 신호를 보내는 <b>단 하나의</b> 경로 (§3.4). 두 곳이 이걸 쓴다 —
/// 중복 실행을 발견한 워크스페이스 프로세스, 그리고 런처의 "창 보이기".
/// </summary>
/// <remarks>
/// <para>
/// 수단은 Stage 0 이 실측한 조합이다: <b>단방향 named pipe + 보내는 쪽의
/// <c>AllowSetForegroundWindow(ASFW_ANY)</c></b>. 창을 올리는 것은 받는 쪽이 한다
/// (<see cref="WindowActivation"/>) — 창을 아는 쪽이 자기 창을 올리는 편이 경계가 깨끗하고,
/// hwnd 를 프로세스 밖으로 내보내지 않아도 된다. pid 를 알 필요가 없으므로 손잡기(양방향)도
/// 필요 없다.
/// </para>
/// <para>
/// <b>이 함수는 어떤 경우에도 돌아온다.</b> §3.2-3 의 좀비 처리가 "신호 실패"라는 결과 위에 서
/// 있으므로, 여기서 멈추면 워크스페이스가 아예 안 열린다. 실측된 위험이 하나 있다 —
/// <b>붙기는 했는데 상대가 읽지 않으면 쓰기가 무한히 막힌다</b>(같은 프로세스 안에서 재현).
/// 그래서 파이프를 <c>Asynchronous</c> 로 열어 연결·쓰기를 전부 취소 가능하게 하고, 그 위에
/// 기다림 자체에도 한도를 둔다.
/// </para>
/// </remarks>
public static class ActivationSignal
{
    /// <summary>보내는 한 줄. 받는 쪽은 <b>이 문자열일 때만</b> 창을 올린다.</summary>
    /// <remarks>
    /// 내용을 확인하는 이유(실측): <c>File.Exists(@"\\.\pipe\이름")</c> 은 파이프를 <b>열어</b>
    /// 존재를 확인한다 — 즉 남이 존재 확인만 해도 서버에는 연결이 하나 들어온다(본문 없음).
    /// 본문을 보고 판정하면 그런 연결에 창이 튀어나오지 않는다.
    /// </remarks>
    public const string ActivatePayload = "activate";

    /// <summary>
    /// "이 인스턴스가 문을 닫는다" 한 줄. 워크스페이스 프로세스가 <b>끝나면서</b> 런처에게
    /// 보낸다 — 받는 쪽은 창을 올리지 않고 배지만 다시 잰다.
    /// </summary>
    /// <remarks>
    /// 이게 없으면 배지가 낡은 채로 남는다. 실측한 두 경로 —
    /// <list type="number">
    /// <item>워크스페이스 창이 닫히면 런처가 곧바로 활성화되어 재검사가 도는데, 창이 사라진
    /// 시점(11ms)과 파이프가 사라지는 시점(52ms) 사이에 <b>41ms 의 틈</b>이 있다. 그 틈에서
    /// 재검사가 "아직 열려 있다"를 보고, 폴링이 없으니 그대로 굳는다.</item>
    /// <item>창을 닫아도 포그라운드가 런처가 아닌 다른 앱으로 가면 활성화 자체가 오지 않는다.</item>
    /// </list>
    /// 닫는 쪽이 <b>자기 파이프를 닫은 뒤</b> 알리면 두 경로가 같이 닫힌다. 폴링은 여전히 없다.
    /// </remarks>
    public const string ClosedPayload = "closed";

    /// <summary>
    /// 연결을 기다리는 한도. 살아 있는 창이면 1ms 안에 붙는다(실측). 이 시간은 곧
    /// <b>좀비를 판정하는 데 걸리는 시간</b>이라 짧게 둔다 — 그동안 사용자는 아무것도 못 본다.
    /// </summary>
    private const int ConnectTimeoutMs = 500;

    /// <summary>연결부터 쓰기까지 전부 합친 한도.</summary>
    private const int SignalTimeoutMs = 1500;

    /// <summary>
    /// 취소마저 듣지 않는 경우의 마지막 한도. 여기까지 오면 그 작업을 버려 두고 실패로 답한다 —
    /// 스레드 하나를 흘리는 것이 창을 못 여는 것보다 낫다.
    /// </summary>
    private const int AbandonAfterMs = 3000;

    private const uint AsfwAny = 0xFFFFFFFF;

    private static readonly byte[] ActivateBytes = Encoding.UTF8.GetBytes(ActivatePayload + "\n");
    private static readonly byte[] ClosedBytes = Encoding.UTF8.GetBytes(ClosedPayload + "\n");

    /// <summary>
    /// 신호를 보낸다. 성공하면 그쪽 창이 앞으로 온다. 받을 사람이 없거나 응답이 없으면 false.
    /// </summary>
    public static bool TrySend(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        // 포그라운드 권한을 먼저 넘긴다 — 그래야 받는 쪽의 SetForegroundWindow 가
        // 포그라운드 잠금에 걸리지 않는다(Stage 0 실측 조합). 부르는 스레드에서 한다.
        AllowSetForegroundWindow(AsfwAny);

        return Send(pipeName, ActivateBytes);
    }

    /// <summary>
    /// "나는 닫힌다"를 알린다 (<see cref="ClosedPayload"/>). 창을 올리는 신호가 아니므로
    /// <b>포그라운드 권한을 넘기지 않는다</b> — 넘기면 남의 창이 앞으로 튀어나올 수 있다.
    /// </summary>
    /// <remarks>
    /// 받을 사람이 없으면 그냥 false 다. 부르는 쪽은 그것으로 아무것도 하지 않는다 —
    /// 배지가 조금 낡는 것이 종료를 늦추는 것보다 낫다.
    /// </remarks>
    public static bool TryNotifyClosed(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return Send(pipeName, ClosedBytes);
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

            // Asynchronous 라야 연결·쓰기가 취소 가능하다. 동기 핸들이면 쓰기에서 막힌 뒤
            // 아무도 그걸 풀 수 없다.
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            await client.ConnectAsync(ConnectTimeoutMs, cts.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, cts.Token).ConfigureAwait(false);
            await client.FlushAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // 파이프가 없다(창이 없다), 또는 있는데 붙을 자리가 없다.
            return false;
        }
        catch (OperationCanceledException)
        {
            // 붙기는 했는데 아무도 읽지 않는다(좀비).
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
