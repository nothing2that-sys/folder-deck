using System.Runtime.ExceptionServices;
using System.Windows;

namespace FolderDeck.App.Tests;

/// <summary>
/// 실제 WPF 창을 생성해보는 시험(<see cref="BatchRenameDialogConstructionTests"/>·
/// <see cref="DuplicateFinderWindowConstructionTests"/>)이 공유하는 STA 진입점 — 하나로
/// 모아 둔다.
/// </summary>
/// <remarks>
/// <b>왜 하나로 모았나(2026-09-18 advisor 지적):</b> 처음엔 이 헬퍼를 시험 파일마다 복붙
/// 했는데, <c>Application.Current is null</c> 검사와 <c>new Application()</c> 이 파일별로
/// 다른 <c>lock</c> 객체를 썼다 — xUnit 은 기본적으로 <b>서로 다른 클래스(=collection)를
/// 병렬로</b> 돌리므로, 두 클래스의 시험이 겹치면 두 스레드가 동시에 "아직 없다"를 보고
/// 둘 다 <c>new Application()</c> 을 불러 <c>InvalidOperationException</c>("Application
/// 개체를 두 번 이상 만들 수 없습니다")으로 죽을 수 있었다. 처음 실행에서 안 죽은 것은
/// 우연히 안 겹쳤을 뿐이다. 여기 하나로 모으고 <c>lock</c> 도 하나만 쓰면 그 경쟁이 없다.
/// <para>
/// <c>PanelFocusPolicyTests</c>·<c>VirtualizingWrapPanelLayoutTests</c>는 일부러 이
/// 헬퍼로 안 옮겼다 — 그 둘은 <c>Border</c>/<c>Button</c>/<c>TextBlock</c>처럼
/// <c>StaticResource</c>를 안 쓰는 맨 컨트롤만 만들어서 <c>Application.Current</c> 유무와
/// 무관하다. 이 헬퍼가 필요한 건 XAML이 <c>{StaticResource ...}</c>를 쓰는 창(다이얼로그)을
/// 실제로 생성해볼 때뿐이다.
/// </para>
/// </remarks>
internal static class StaWpfTestSupport
{
    private static readonly object AppSetupLock = new();

    public static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureWpfApplicationResources();
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// <c>BatchRenameDialog.xaml</c>·<c>DuplicateFinderWindow.xaml</c>은 둘 다
    /// <c>{StaticResource Brush.*}</c>를 쓴다 — 실제 앱에서는 <c>App.xaml</c>이
    /// <c>Themes/LightTheme.xaml</c>을 병합해 두므로 되지만, 시험 프로세스에는
    /// <c>Application</c>이 아예 없다. 한 번만 만들어 리소스를 병합해 둔다 — 두 번째로
    /// 부르면(다른 시험·다른 스레드) 조용히 건너뛴다.
    /// </summary>
    private static void EnsureWpfApplicationResources()
    {
        lock (AppSetupLock)
        {
            if (Application.Current is not null)
            {
                return;
            }

            // 2026-10-14 S1 — 맨 Application + LightTheme 병합에서 실물 App 의 리소스로 넓혔다.
            // MainWindow.xaml 은 LightTheme 의 Brush.* 말고도 App.xaml 에만 있는 스타일
            // (NavButton·EditModeButton 등)과 변환기를 쓴다. App.xaml 이 LightTheme 를 병합하므로
            // 기존 다이얼로그 시험이 보던 리소스는 그대로 다 있다(상위 집합). StartupUri 가 없고
            // OnStartup 은 Run() 에서만 불리므로 InitializeComponent 는 리소스만 싣는다.
            var app = new FolderDeck.App.App();
            app.InitializeComponent();
        }
    }
}
