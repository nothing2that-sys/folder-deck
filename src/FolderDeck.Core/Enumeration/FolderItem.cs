namespace FolderDeck.Core.Enumeration;

public sealed record FolderItem(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTime ModifiedUtc);
