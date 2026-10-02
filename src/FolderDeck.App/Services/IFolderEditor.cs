namespace FolderDeck.App.Services;

public sealed record FolderEditDraft(string? DisplayName, string? Description);

public interface IFolderEditor
{

    FolderEditDraft? Edit(FolderEditDraft draft, string path);
}
