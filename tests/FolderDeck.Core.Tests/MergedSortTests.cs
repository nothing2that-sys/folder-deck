using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

public sealed class MergedSortTests : IDisposable
{
    private readonly string _root;
    private readonly FolderEnumerator _enumerator = new();

    public MergedSortTests()
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

    [Fact]
    public async Task FoldersMixIntoTheFilesByName()
    {
        Dir_("mmm-folder");
        File_("aaa.txt");
        File_("zzz.txt");

        var listing = await _enumerator.ListAsync(_root, SortBy.Name, foldersFirst: false);

        Assert.Equal(["aaa.txt", "mmm-folder", "zzz.txt"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TheMergedOrderReversesWholeWhenDescending()
    {
        Dir_("mmm-folder");
        File_("aaa.txt");
        File_("zzz.txt");

        var listing = await _enumerator.ListAsync(
            _root, SortBy.Name, sortDesc: true, foldersFirst: false);

        Assert.Equal(["zzz.txt", "mmm-folder", "aaa.txt"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task FoldersMixIntoTheFilesByModified()
    {
        var dir = Dir_("middle");
        Directory.SetLastWriteTimeUtc(dir, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File_("old.txt", modified: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File_("new.txt", modified: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var listing = await _enumerator.ListAsync(_root, SortBy.Modified, foldersFirst: false);

        Assert.Equal(["old.txt", "middle", "new.txt"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task SizeStillGroupsFoldersBecauseTheyHaveNoSize()
    {
        Dir_("zzz-folder");
        File_("big.txt", new string('x', 5000));
        File_("small.txt", new string('x', 10));

        var listing = await _enumerator.ListAsync(_root, SortBy.Size, foldersFirst: false);

        Assert.Equal(["zzz-folder", "small.txt", "big.txt"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TheDefaultIsStillFoldersFirst()
    {
        Dir_("zzz-folder");
        File_("aaa.txt");

        var listing = await _enumerator.ListAsync(_root, SortBy.Name);

        Assert.Equal(["zzz-folder", "aaa.txt"], listing.Items.Select(i => i.Name));
    }

    private void File_(string name, string contents = "", DateTime? modified = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        if (modified is not null)
        {
            File.SetLastWriteTimeUtc(path, modified.Value);
        }
    }

    private string Dir_(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
