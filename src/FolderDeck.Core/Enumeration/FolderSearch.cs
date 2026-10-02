namespace FolderDeck.Core.Enumeration;

public sealed record FolderSearchHit(FolderItem Item, string RelativeFolder);

public sealed class FolderSearchStats
{
    private int _foldersScanned;
    private int _foldersSkipped;

    public int FoldersScanned => Volatile.Read(ref _foldersScanned);

    public int FoldersSkipped => Volatile.Read(ref _foldersSkipped);

    internal void CountScanned() => Interlocked.Increment(ref _foldersScanned);

    internal void CountSkipped() => Interlocked.Increment(ref _foldersSkipped);
}
