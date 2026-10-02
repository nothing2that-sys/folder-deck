using System.Runtime.InteropServices;
using System.Threading;

namespace FolderDeck.App.Interop;























internal static class FpuGuard
{




    internal const uint MaskEm = 0x0008001F;

    private static long _restoreCount;


    internal static long RestoreCount => Interlocked.Read(ref _restoreCount);








    internal static uint? Current() => ControlFpS(out uint cur, 0, 0) == 0 ? cur : null;


    internal static bool SetExceptionMask(uint em) => ControlFpS(out _, em & MaskEm, MaskEm) == 0;






    internal static Scope Enter(string tag)
    {
        Restore(tag, "진입");
        return new Scope(tag);
    }

















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









    internal readonly struct Scope : IDisposable
    {
        private readonly string _tag;

        internal Scope(string tag) => _tag = tag;

        public void Dispose() => Restore(_tag, "이탈");
    }



    [DllImport("ucrtbase.dll", EntryPoint = "_controlfp_s")]
    private static extern int ControlFpS(out uint currentControl, uint newControl, uint mask);
}
