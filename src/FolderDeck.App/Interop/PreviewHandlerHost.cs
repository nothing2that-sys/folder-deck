using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FolderDeck.App.Interop;

internal enum PreviewHandlerOutcome
{

    Shown,

    NoHandler,

    Failed,
}

internal class PreviewHandlerHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;

    private IntPtr _hwndHost = IntPtr.Zero;
    private object? _handlerObj;
    private IPreviewHandler? _handler;

    internal string LastResult { get; private set; } = "(아직 시도 안 함)";

    internal string LastSource { get; private set; } = "";

    internal string LastActivationMode { get; private set; } = "";

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwndHost = CreateWindowEx(
            0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 200, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        UnloadPreview();
        DestroyWindow(hwnd.Handle);
        _hwndHost = IntPtr.Zero;
    }

    internal PreviewHandlerOutcome LoadPreview(string filePath, int width, int height)
    {
        UnloadPreview();

        Guid? clsid = PreviewHandlerResolver.FindHandlerClsid(filePath, out var source);
        LastSource = source;
        if (clsid is null)
        {
            LastResult = "핸들러 없음 (레지스트리에 shellex 항목 없음)";
            return PreviewHandlerOutcome.NoHandler;
        }

        try
        {
            using (FpuGuard.Enter("IPreviewHandler"))
            {
                _handlerObj = TryActivateOutOfProcess(clsid.Value);
                if (_handlerObj is not null)
                {
                    LastActivationMode = "별도 프로세스";
                }
                else
                {
                    LastActivationMode = "우리 프로세스";
                    Type? comType = Type.GetTypeFromCLSID(clsid.Value);
                    _handlerObj = comType is null ? null : Activator.CreateInstance(comType);
                }

                if (_handlerObj is null)
                {
                    LastResult = $"CLSID {clsid} 를 못 얻음(서로게이트·인프로세스 둘 다 실패)";
                    return PreviewHandlerOutcome.Failed;
                }

                bool initialized = TryInitializeWithItem(filePath) || TryInitializeWithFile(filePath);
                if (!initialized)
                {
                    LastResult = "IInitializeWithItem·IInitializeWithFile 둘 다 못 얻음";
                    ReleaseHandlerObj();
                    return PreviewHandlerOutcome.Failed;
                }

                _handler = (IPreviewHandler)_handlerObj;
                var rect = new RECT { Left = 0, Top = 0, Right = Math.Max(width, 1), Bottom = Math.Max(height, 1) };
                _handler.SetWindow(_hwndHost, ref rect);
                _handler.DoPreview();
                LastResult = "떴다";
                return PreviewHandlerOutcome.Shown;
            }
        }
        catch (Exception ex)
        {
            LastResult = "예외: " + ex.GetType().Name + " " + ex.Message;
            ReleaseHandlerObj();
            return PreviewHandlerOutcome.Failed;
        }
    }

    private bool TryInitializeWithItem(string filePath)
    {
        if (_handlerObj is not IInitializeWithItem initItem)
            return false;

        object? shellItemObj = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            int hr = ShellItemNative.SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out shellItemObj);
            if (hr != 0 || shellItemObj is not IShellItem shellItem)
                return false;
            initItem.Initialize(shellItem, 0                );
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {

            if (shellItemObj != null)
            {
                try { Marshal.ReleaseComObject(shellItemObj); } catch { }
            }
        }
    }

    private bool TryInitializeWithFile(string filePath)
    {
        if (_handlerObj is not IInitializeWithFile initFile)
            return false;
        try
        {
            initFile.Initialize(filePath, 0                );
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void UnloadPreview()
    {
        if (_handler != null)
        {

            using (FpuGuard.Enter("IPreviewHandler.Unload"))
            {
                try { _handler.Unload(); } catch { }
            }

            _handler = null;
        }

        ReleaseHandlerObj();
    }

    private void ReleaseHandlerObj()
    {
        if (_handlerObj != null)
        {

            using (FpuGuard.Enter("IPreviewHandler.Release"))
            {
                try { Marshal.ReleaseComObject(_handlerObj); } catch { }
            }

            _handlerObj = null;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_handler is null)
        {
            return;
        }

        var rect = new RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Max(sizeInfo.NewSize.Width, 1),
            Bottom = (int)Math.Max(sizeInfo.NewSize.Height, 1),
        };

        using (FpuGuard.Enter("IPreviewHandler.SetRect"))
        {
            try { _handler.SetRect(ref rect); } catch { }
        }
    }

    internal static object? TryActivateOutOfProcess(Guid clsid)
    {
        try
        {
            var iid = typeof(IPreviewHandler).GUID;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out var ppv);
            if (hr == CO_E_SERVER_EXEC_FAILURE)
            {

                hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out ppv);
            }

            if (hr < 0 || ppv == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.GetUniqueObjectForIUnknown(ppv);
            }
            finally
            {
                Marshal.Release(ppv);
            }
        }
        catch
        {

            return null;
        }
    }

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const int CO_E_SERVER_EXEC_FAILURE = unchecked((int)0x80080005);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr hwndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
