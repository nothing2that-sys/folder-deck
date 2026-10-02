using FolderDeck.App;
using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;

public class FpuGuardTests
{

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

            Assert.Equal(before, FpuGuard.RestoreCount);
        }
        finally
        {
            FpuGuard.SetExceptionMask(saved);
        }
    }

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

    private static uint Require(uint? current)
    {
        Assert.NotNull(current);
        return current.Value;
    }
}
