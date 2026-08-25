using System.Linq;
using System.Windows;
using System.Windows.Input;
using FolderDeck.App.ViewModels;
using IoDirectory = System.IO.Directory;

namespace FolderDeck.App;

/// <summary>런처 창 (§3.1).</summary>
public partial class LauncherWindow : Window
{
    public LauncherWindow(LauncherViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
    }

    public LauncherViewModel ViewModel { get; }

    /// <summary>
    /// 창으로 돌아오면 "열려 있음" 배지를 다시 잰다 (§4.2 — 폴링하지 않는다).
    /// §8 의 기존 결(수동 F5 + 창 활성화 시 재조회)과 같은 자리다.
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ViewModel.RefreshOpenState();
    }

    /// <summary>
    /// 카드 한 번 클릭 = 선택, 두 번 = 열기 (Phase 8-A ④).
    /// </summary>
    /// <remarks>
    /// <b>여는 것은 <see cref="LauncherViewModel.Open"/> 하나뿐이다</b> — "열기" 버튼과 같은
    /// 자리로 들어가므로 §6.2 의 "이미 열려 있으면 아무 일도 하지 않는다"가 여기서도 그대로
    /// 적용된다. 이 핸들러는 그 판정을 다시 하지 않는다.
    /// <para>
    /// 안쪽 <c>Button</c>·<c>TextBox</c> 는 마우스 이벤트를 스스로 처리하므로 여기까지
    /// 올라오지 않는다. "열기"를 누른 것이 선택으로도 세어지는 일은 그래서 없다.
    /// </para>
    /// </remarks>
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkspaceCardViewModel card })
        {
            return;
        }

        ViewModel.Select(card);

        if (e.ClickCount == 2)
        {
            ViewModel.Open(card);
        }
    }

    /// <summary>폴더를 창에 끌어다 놓으면 "새로 만들기"에 담는다. 파일은 무시한다.</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolders(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        var folders = DroppedFolders(e);
        if (folders.Count == 0)
        {
            return;
        }

        // 드롭만으로 새로 만들기가 열리게 한다 — 버튼을 먼저 누르지 않아도 되도록.
        if (!ViewModel.IsCreating)
        {
            ViewModel.StartCreateCommand.Execute(null);
        }

        ViewModel.AddFolders(folders);
        e.Handled = true;
    }

    private static List<string> DroppedFolders(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? [.. paths.Where(IoDirectory.Exists)]
            : [];
    }
}
