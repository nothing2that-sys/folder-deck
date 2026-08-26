using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

/// <summary>
/// <c>foldersFirst: false</c> — 폴더와 파일을 한 덩어리로 정렬한다 (Phase 8-B ②).
/// </summary>
/// <remarks>
/// 기본값은 <c>true</c> 이고 그것이 이 앱이 원래 하던 동작이다 —
/// <see cref="FolderEnumeratorTests"/> 의 판들이 인자 없이 그 동작을 이미 못박고 있다.
/// 여기 있는 것은 <b>끈 쪽</b>뿐이다.
/// </remarks>
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
            // 정리 실패는 무시.
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

    /// <summary>
    /// <b>크기만은 통합해도 폴더가 뭉친다.</b> 폴더 크기를 재귀로 재지 않으므로(§2.2)
    /// <c>Size</c> 가 <c>null</c> 이고, <c>Nullable.Compare</c> 는 null 을 앞에 둔다.
    /// 숨은 동작이 아니라 <b>정해진 결과</b>라 여기에 적어 둔다.
    /// </summary>
    [Fact]
    public async Task SizeStillGroupsFoldersBecauseTheyHaveNoSize()
    {
        Dir_("zzz-folder");
        File_("big.txt", new string('x', 5000));
        File_("small.txt", new string('x', 10));

        var listing = await _enumerator.ListAsync(_root, SortBy.Size, foldersFirst: false);

        Assert.Equal(["zzz-folder", "small.txt", "big.txt"], listing.Items.Select(i => i.Name));
    }

    /// <summary>기본값은 켠 것이다 — 인자를 안 주면 폴더가 먼저다.</summary>
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
