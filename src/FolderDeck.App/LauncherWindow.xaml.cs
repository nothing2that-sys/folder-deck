using System.Linq;
using System.Windows;
using System.Windows.Input;
using FolderDeck.App.ViewModels;
using IoDirectory = System.IO.Directory;

namespace FolderDeck.App;

public partial class LauncherWindow : Window
{
    public LauncherWindow(LauncherViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
    }

    public LauncherViewModel ViewModel { get; }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ViewModel.RefreshOpenState();
    }

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
