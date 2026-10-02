namespace FolderDeck.App.Services;

public interface IFolderPicker
{

    IReadOnlyList<string> PickFolders(string title);
}

public sealed class FolderPicker : IFolderPicker
{
    public IReadOnlyList<string> PickFolders(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = true,
        };

        return dialog.ShowDialog() == true ? dialog.FolderNames : [];
    }
}
