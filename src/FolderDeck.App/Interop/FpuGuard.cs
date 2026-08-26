using System.Runtime.InteropServices;
using System.Threading;

namespace FolderDeck.App.Interop;

/// <summary>
/// 셸 확장이 망쳐 놓은 <b>부동소수점 예외 마스크</b>를 되돌린다 (조사 E-5 · 고침 F-1).
/// </summary>
/// <remarks>
/// 우클릭이 셸 확장 DLL 을 in-proc 로 올리는데, 그중 하나가 UI 스레드의 마스크를 풀고
/// 복원하지 않는다(실측: <c>_MCW_EM</c> 의 Invalid·ZeroDivide·Overflow 세 비트가 꺼짐).
/// 그 뒤 레이아웃의 <b>정상적인</b> <c>divsd</c> 가 0 으로 나누면 하드웨어 예외
/// <c>0xC000008E</c> 가 되고, .NET 10 의 <c>RhThrowHwEx</c> 매핑표에 그 코드가 없어
/// <c>FailFast("InternalError")</c> 로 프로세스가 즉사한다.
/// <para>
/// <b>우리가 잡을 수 있는 고리는 이것 하나다.</b> 확장도 못 고치고 .NET 매핑표도 못 바꾼다.
/// </para>
/// <para>
/// <b>진입과 이탈 양쪽에서 되돌린다.</b> 이탈은 이 회차의 고침이고, 진입은 열거에서 자리를
/// 빠뜨렸을 때의 그물이다 — 둘 다 「풀려 있을 때만」 손대므로 평소에는 읽기 한 번이다.
/// </para>
/// <para>
/// <b>확인된 것은 <c>_controlfp_s</c> 로 읽히는 값뿐이다.</b> x64 관리 코드의 부동소수점은
/// SSE 를 쓰므로 요점은 <c>MxCsr</c> 이고, 이 호출이 x87 제어워드까지 함께 다루는지는
/// 확인하지 않았다.
/// </para>
/// </remarks>
internal static class FpuGuard
{
    /// <summary>
    /// <c>_MCW_EM</c> — 예외 마스크 여섯 비트(Invalid·Denormal·ZeroDivide·Overflow·
    /// Underflow·Inexact). 비트가 <b>켜져</b> 있으면 그 예외가 <b>막혀</b> 있다(정상).
    /// </summary>
    internal const uint MaskEm = 0x0008001F;

    private static long _restoreCount;

    /// <summary>실제로 되돌린 횟수. 0 이면 이 실행에서 아무도 마스크를 풀지 않았다.</summary>
    internal static long RestoreCount => Interlocked.Read(ref _restoreCount);

    /// <summary>
    /// 지금 이 스레드의 제어워드. 읽지 못하면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>_controlfp_s</c> 선언을 이 파일 하나로 묶기 위한 통로다 — 부르는 쪽이 P/Invoke 를
    /// 따로 선언하지 않게 한다(F-1 §4 범위 계수).
    /// </remarks>
    internal static uint? Current() => ControlFpS(out uint cur, 0, 0) == 0 ? cur : null;

    /// <summary>예외 마스크 여섯 비트를 <paramref name="em"/> 로 맞춘다. 성공하면 true.</summary>
    internal static bool SetExceptionMask(uint em) => ControlFpS(out _, em & MaskEm, MaskEm) == 0;

    /// <summary>
    /// 제3자 코드가 돌 수 있는 호출을 감싼다. <paramref name="tag"/> 는 로그에만 쓴다 —
    /// <b>어느 호출이 마스크를 푸는지</b>가 그 값으로 드러난다(E-5 §6.2).
    /// </summary>
    /// <remarks>경로 문자열을 넘기지 마라 — 개인 경로가 로그에 남는다.</remarks>
    internal static Scope Enter(string tag)
    {
        Restore(tag, "진입");
        return new Scope(tag);
    }

    /// <summary>
    /// 마스크가 하나라도 풀려 있으면 전부 켠다. 안 풀렸으면 아무것도 하지 않는다 —
    /// 뜨거운 경로에서 불릴 수 있다.
    /// </summary>
    /// <remarks>
    /// <b>여기 있던 계측을 걷어냈다</b>(E-2 · <c>ShellMenuProbe</c>). 그때는 읽기 실패 ·
    /// 되돌리기 실패 · 검산 결과를 한 줄씩 파일에 적었고, 검산을 위해 되돌린 값을 다시 읽었다.
    /// 지금은 <b>고치는 손만 남았다</b> — 되돌림이 몇 번 일어났는지는 <see cref="RestoreCount"/>
    /// 가 세고, 그 이상은 아무 데도 남지 않는다.
    /// <para>
    /// <paramref name="tag"/>·<paramref name="when"/> 은 그 로그가 쓰던 값이라 지금은 읽는 곳이
    /// 없다. <b>일부러 남겼다</b>: 태그는 <see cref="Enter"/> 호출 17곳에서 「무엇을 감싸는
    /// 스코프인가」를 말하는 유일한 글자이고(디버거로 <see cref="Scope"/> 를 열면 보인다),
    /// 떼면 프로브 제거와 무관한 17곳 + 시험 넷의 손질이 이 회차에 섞인다.
    /// </para>
    /// </remarks>
    private static void Restore(string tag, string when)
    {
        if (Current() is not { } before)
        {
            return;
        }

        if ((before & MaskEm) == MaskEm)
        {
            return;
        }

        Interlocked.Increment(ref _restoreCount);
        SetExceptionMask(MaskEm);
    }

    /// <summary>
    /// <see cref="Enter"/> 가 돌려주는 스코프. <c>Dispose</c> 에서 한 번 더 되돌린다.
    /// </summary>
    /// <remarks>
    /// <b><c>Marshal.GetLastPInvokeError()</c> 를 읽는 자리보다 먼저 놓지 마라</b> —
    /// <c>_controlfp_s</c> 가 last error 를 덮는다. 스코프가 그 읽기를 <b>안에</b> 두도록
    /// 배치했다(F-1 §3 정지 조건).
    /// </remarks>
    internal readonly struct Scope : IDisposable
    {
        private readonly string _tag;

        internal Scope(string tag) => _tag = tag;

        public void Dispose() => Restore(_tag, "이탈");
    }

    // 손으로 쓴 [DllImport] 다 — Interop/ 의 나머지와 같은 방식이다(ShellNative 주석).
    // errno 를 돌려준다(0 = 성공). mask 가 0 이면 읽기만 한다.
    [DllImport("ucrtbase.dll", EntryPoint = "_controlfp_s")]
    private static extern int ControlFpS(out uint currentControl, uint newControl, uint mask);
}
