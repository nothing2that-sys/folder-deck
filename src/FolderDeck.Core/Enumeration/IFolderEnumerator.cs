using FolderDeck.Core.Models;

namespace FolderDeck.Core.Enumeration;

public interface IFolderEnumerator
{

    Task<FolderListing> ListAsync(
        string path,
        SortBy sortBy = SortBy.Name,
        bool sortDesc = false,
        bool foldersFirst = true,
        CancellationToken cancellationToken = default);

    Task<FolderAccessFailure?> ProbeAsync(string path, CancellationToken cancellationToken = default);

    IAsyncEnumerable<FolderSearchHit> SearchAsync(
        string root,
        string query,
        FolderSearchStats stats,
        CancellationToken cancellationToken = default);
}
