using System.Linq;
using System.Windows;
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
