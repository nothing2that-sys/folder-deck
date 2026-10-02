using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

public sealed class FolderEnumeratorTests : IDisposable
{
    private readonly string _root;
    private readonly FolderEnumerator _enumerator = new();

    public FolderEnumeratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {

        }
    }

    private string File_(string name, string contents = "", DateTime? modified = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        if (modified is not null)
        {
            File.SetLastWriteTimeUtc(path, modified.Value);
        }

        return path;
    }

    private string Dir_(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task ReturnsFilesAndSubfoldersWithMetadata()
    {
        Dir_("sub");
        File_("a.txt", "12345");

        var listing = await _enumerator.ListAsync(_root);

        Assert.True(listing.IsSuccess);
        Assert.Equal(_root, listing.Path);
        Assert.Equal(2, listing.Items.Count);

        var dir = listing.Items.Single(i => i.IsDirectory);
        Assert.Equal("sub", dir.Name);
        Assert.Equal(Path.Combine(_root, "sub"), dir.FullPath);
        Assert.Null(dir.Size);

        var file = listing.Items.Single(i => !i.IsDirectory);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal(5, file.Size);
        Assert.True((DateTime.UtcNow - file.ModifiedUtc).Duration() < TimeSpan.FromMinutes(5));
        Assert.Equal(DateTimeKind.Utc, file.ModifiedUtc.Kind);
    }

    [Fact]
    public async Task DoesNotRecurse()
    {
        var sub = Dir_("sub");
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "x");

        var listing = await _enumerator.ListAsync(_root);

        Assert.Equal("sub", Assert.Single(listing.Items).Name);
    }

    [Fact]
    public async Task EmptyFolderSucceedsWithNoItems()
    {
        var listing = await _enumerator.ListAsync(_root);

        Assert.True(listing.IsSuccess);
        Assert.Empty(listing.Items);
    }

    [Fact]
    public async Task FoldersComeFirstEvenWhenDescending()
    {
        Dir_("zzz-folder");
        File_("aaa-file.txt");

        foreach (var desc in new[] { false, true })
        {
            var listing = await _enumerator.ListAsync(_root, SortBy.Name, desc);

            Assert.True(listing.Items[0].IsDirectory, $"sortDesc={desc} 에서도 폴더가 먼저여야 한다.");
            Assert.False(listing.Items[1].IsDirectory);
        }
    }

    [Fact]
    public async Task SortsByNameAscendingAndDescending()
    {
        File_("b.txt");
        File_("a.txt");
        File_("c.txt");

        var asc = await _enumerator.ListAsync(_root, SortBy.Name);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], asc.Items.Select(i => i.Name));

        var desc = await _enumerator.ListAsync(_root, SortBy.Name, sortDesc: true);
        Assert.Equal(["c.txt", "b.txt", "a.txt"], desc.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task SortsBySize()
    {
        File_("small.txt", new string('x', 10));
        File_("big.txt", new string('x', 5000));
        File_("mid.txt", new string('x', 500));

        var asc = await _enumerator.ListAsync(_root, SortBy.Size);
        Assert.Equal(["small.txt", "mid.txt", "big.txt"], asc.Items.Select(i => i.Name));

        var desc = await _enumerator.ListAsync(_root, SortBy.Size, sortDesc: true);
        Assert.Equal(["big.txt", "mid.txt", "small.txt"], desc.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task SortsByModified()
    {
        File_("old.txt", modified: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File_("new.txt", modified: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File_("mid.txt", modified: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var asc = await _enumerator.ListAsync(_root, SortBy.Modified);
        Assert.Equal(["old.txt", "mid.txt", "new.txt"], asc.Items.Select(i => i.Name));

        var desc = await _enumerator.ListAsync(_root, SortBy.Modified, sortDesc: true);
        Assert.Equal(["new.txt", "mid.txt", "old.txt"], desc.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task SortsByTypeThenNameWithinTheSameExtension()
    {
        File_("b.log");
        File_("a.log");
        File_("c.cs");

        var listing = await _enumerator.ListAsync(_root, SortBy.Type);

        Assert.Equal(["c.cs", "a.log", "b.log"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task MissingFolderIsAFailureNotAnException()
    {
        var listing = await _enumerator.ListAsync(Path.Combine(_root, "없는-하위폴더"));

        Assert.False(listing.IsSuccess);
        Assert.Equal(FolderAccessFailureKind.NotFound, listing.Failure!.Kind);
        Assert.Empty(listing.Items);
    }

    [Fact]
    public async Task UnreachableNetworkPathReportsUnavailable()
    {
        var listing = await _enumerator.ListAsync(@"\\folderdeck-no-such-host\share");

        Assert.False(listing.IsSuccess);
        Assert.Contains(
            listing.Failure!.Kind,
            new[] { FolderAccessFailureKind.Unavailable, FolderAccessFailureKind.NotFound });
        Assert.Equal(@"\\folderdeck-no-such-host\share", listing.Failure.Path);
    }

    [Fact]
    public async Task ProbeReturnsNullWhenReachable()
    {
        Assert.Null(await _enumerator.ProbeAsync(_root));
    }

    [Fact]
    public async Task ProbeReportsMissingFolder()
    {
        var failure = await _enumerator.ProbeAsync(Path.Combine(_root, "없다"));

        Assert.Equal(FolderAccessFailureKind.NotFound, failure!.Kind);
    }

    [Fact]
    public async Task ProbeSucceedsOnAnEmptyFolder()
    {
        Assert.Null(await _enumerator.ProbeAsync(Dir_("empty")));
    }

    [Fact]
    public async Task AlreadyCancelledRequestThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _enumerator.ListAsync(_root, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LargeFolderStillEnumerates()
    {
        for (var i = 0; i < 2000; i++)
        {
            File_($"f{i:D4}.log");
        }

        var listing = await _enumerator.ListAsync(_root, SortBy.Name);

        Assert.Equal(2000, listing.Items.Count);
        Assert.Equal("f0000.log", listing.Items[0].Name);
    }
}
