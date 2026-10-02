namespace FolderDeck.Core.Models;

public enum FolderViewMode
{
    List,
    Details,
    LargeIcons,
    ExtraLargeIcons,
}

public enum SortBy
{
    Name,
    Modified,
    Size,
    Type,
}

public enum FileOperationKind
{
    Copy,
    Move,
    Trash,
}

public enum ConflictPolicy
{
    Overwrite,
    Skip,
    Rename,
}
