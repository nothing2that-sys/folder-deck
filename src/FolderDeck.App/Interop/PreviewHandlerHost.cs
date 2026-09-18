using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FolderDeck.App.Interop;

/// <summary>① 단(<see cref="IPreviewHandler"/> 호스팅)의 결과(Phase 17 · D103).</summary>
internal enum PreviewHandlerOutcome
{
    /// <summary>핸들러가 떴다.</summary>
    Shown,

    /// <summary>이 확장자에 미리보기 핸들러가 등록돼 있지 않다.</summary>
    NoHandler,

    /// <summary>핸들러는 찾았으나 만들지·초기화하지·띄우지 못했다.</summary>
    Failed,
}

/// <summary>
/// <see cref="IPreviewHandler"/> 를 얹는 최소 <see cref="HwndHost"/>(Phase 17 · D103).
/// <c>_spike_preview_D85\Interop\PreviewHandlerHost.cs</c> 에서 옮겼다. 서로게이트(별도
/// 프로세스) 활성화 갈래는 가져오지 않는다 — 격리가 안 된다는 것이 실측이다(D85-d).
/// 자식 HWND 는 커스텀 윈도우 클래스를 등록하지 않고 내장 "static" 클래스로 만든다.
/// </summary>
/// <remarks>
/// <see cref="LoadPreview"/> 안에서는 <c>catch (Exception)</c> 로 넓게 잡는다 — 이 저장소의
/// 다른 파일들은 예외 종류를 좁혀 잡는 관례지만, 여기서 부르는 것은 <b>서드파티 COM 코드</b>
/// (등록된 미리보기 핸들러)라 무엇을 던질지 우리가 알 수 없다. 그래도 삼키고 끝내지 않는다 —
/// <see cref="LastResult"/> 에 예외 종류와 메시지를 남긴다.
/// </remarks>
internal class PreviewHandlerHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;

    private IntPtr _hwndHost = IntPtr.Zero;
    private object? _handlerObj;
    private IPreviewHandler? _handler;

    /// <summary>진단·보고용 — 화면 글귀는 <see cref="PreviewHandlerOutcome"/> 으로 고른다.</summary>
    internal string LastResult { get; private set; } = "(아직 시도 안 함)";

    internal string LastSource { get; private set; } = "";

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

    /// <summary>
    /// <paramref name="width"/>·<paramref name="height"/> 는 호출자가 이미 잰 칸 크기다 —
    /// 레이아웃이 아직 안 돌았으면 <see cref="FrameworkElement.ActualWidth"/> 가 0 이라 여기서
    /// 다시 재지 않는다.
    /// </summary>
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
                Type? comType = Type.GetTypeFromCLSID(clsid.Value);
                if (comType is null)
                {
                    LastResult = $"CLSID {clsid} 를 Type 으로 못 얻음";
                    return PreviewHandlerOutcome.Failed;
                }

                _handlerObj = Activator.CreateInstance(comType);
                if (_handlerObj is null)
                {
                    LastResult = "CreateInstance 실패(null)";
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
            initItem.Initialize(shellItem, 0 /* STGM_READ */);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // ShellThumbnailService.Fetch 와 같은 이유로 우리가 얻은 IShellItem 은 우리가
            // 놓는다(검토 회차 발견 — 지금까지는 해제 없이 GC 에 미뤄져 있었다).
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
            initFile.Initialize(filePath, 0 /* STGM_READ */);
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
            // 확장의 Unload() 도 서드파티 코드다 — FpuGuard 로 감싼다(검토 회차 발견,
            // ShellContextMenu 와 같은 관례). 이 메서드는 LoadPreview 진입부(72행)처럼
            // 바깥 FpuGuard 스코프 밖에서도 불리므로 여기서 직접 감싼다.
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
            // Release 다 — 그 안에서 확장의 정리 코드가 돈다(ShellContextMenu.Dispose 와
            // 같은 이유, 검토 회차 발견).
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

        // HwndHost 가 자식 창 크기는 스스로 맞춘다. 핸들러에게는 따로 알려야 한다.
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

        // 서드파티 확장 코드가 도는 자리라 FpuGuard 로 감싼다(검토 회차 발견).
        using (FpuGuard.Enter("IPreviewHandler.SetRect"))
        {
            try { _handler.SetRect(ref rect); } catch { }
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr hwndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
