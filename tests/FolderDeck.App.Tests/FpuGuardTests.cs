using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="FpuGuard"/> — 셸 확장이 푼 부동소수점 예외 마스크를 되돌리는가 (고침 F-1).
/// </summary>
/// <remarks>
/// <b>마스크가 풀린 상태에서 부동소수점 연산을 하지 않는다.</b> 0 나눗셈을 실제로 일으키면
/// 조사 E-5 가 확정한 그 경로대로 <c>FailFast</c> 가 나서 <b>테스트 호스트가 죽고 나머지
/// 시험 전부가 보고 없이 사라진다.</b> 여기서 다루는 것은 제어워드 비트뿐이다 —
/// 종단 재현은 손 확인의 몫이다.
/// <para>
/// 제어워드는 <b>스레드별</b>이라 xunit 의 병렬 실행과 무관하다. 그래도 각 시험이
/// <c>finally</c> 로 원래 값을 되돌린다 — 이 스레드를 물려받는 다음 시험이 풀린 마스크를
/// 만나면 그쪽이 죽는다.
/// </para>
/// </remarks>
public class FpuGuardTests
{
    /// <summary>E-5 가 실측한 조합 — Invalid · ZeroDivide · Overflow 만 마스크를 푼다.</summary>
    private const uint Unmasked = FpuGuard.MaskEm & ~(EmInvalid | EmZeroDivide | EmOverflow);

    private const uint EmInvalid = 0x00000010;
    private const uint EmZeroDivide = 0x00000008;
    private const uint EmOverflow = 0x00000004;

    [Fact]
    public void 스코프를_닫으면_풀린_마스크를_되돌린다()
    {
        uint saved = Require(FpuGuard.Current());

        try
        {
            using (FpuGuard.Enter("시험:이탈"))
            {
                // 스코프 안에서 푼다 — 이걸 되돌리는 것이 Dispose 의 일이다.
                Assert.True(FpuGuard.SetExceptionMask(Unmasked));
                Assert.NotEqual(FpuGuard.MaskEm, Require(FpuGuard.Current()) & FpuGuard.MaskEm);
            }

            Assert.Equal(FpuGuard.MaskEm, Require(FpuGuard.Current()) & FpuGuard.MaskEm);
        }
        finally
        {
            FpuGuard.SetExceptionMask(saved);
        }
    }

    [Fact]
    public void 스코프에_들어갈_때도_되돌린다()
    {
        uint saved = Require(FpuGuard.Current());

        try
        {
            // 자리를 빠뜨렸을 때의 그물이다 — 진입만으로도 막혀 있어야 한다.
            Assert.True(FpuGuard.SetExceptionMask(Unmasked));

            using (FpuGuard.Enter("시험:진입"))
            {
                Assert.Equal(FpuGuard.MaskEm, Require(FpuGuard.Current()) & FpuGuard.MaskEm);
            }
        }
        finally
        {
            FpuGuard.SetExceptionMask(saved);
        }
    }

    [Fact]
    public void 이미_막혀_있으면_되돌림을_세지_않는다()
    {
        uint saved = Require(FpuGuard.Current());

        try
        {
            Assert.True(FpuGuard.SetExceptionMask(FpuGuard.MaskEm));

            long before = FpuGuard.RestoreCount;

            using (FpuGuard.Enter("시험:무동작"))
            {
            }

            // 뜨거운 경로에 쓸 수 있어야 한다 — 안 풀렸으면 쓰지도, 세지도 않는다.
            Assert.Equal(before, FpuGuard.RestoreCount);
        }
        finally
        {
            FpuGuard.SetExceptionMask(saved);
        }
    }

    /// <summary>
    /// 마스크 상수가 <c>_MCW_EM</c> 여섯 비트를 그대로 담고 있는가 — 값이 흔들리면 위 셋이
    /// 조용히 무의미해진다.
    /// </summary>
    [Fact]
    public void 마스크_상수는_MCW_EM_여섯_비트다()
    {
        const uint EmDenormal = 0x00080000;
        const uint EmUnderflow = 0x00000002;
        const uint EmInexact = 0x00000001;

        Assert.Equal(
            EmInvalid | EmDenormal | EmZeroDivide | EmOverflow | EmUnderflow | EmInexact,
            FpuGuard.MaskEm);

        Assert.Equal(0x0008001Fu, FpuGuard.MaskEm);
    }

    /// <summary>제어워드를 못 읽으면 시험이 성립하지 않는다 — 그 자리에서 세운다.</summary>
    private static uint Require(uint? current)
    {
        Assert.NotNull(current);
        return current.Value;
    }
}
