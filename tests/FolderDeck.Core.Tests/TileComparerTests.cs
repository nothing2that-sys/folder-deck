using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Tests;

public sealed class TileComparerTests
{
    private static FolderItem File(string name, long size, DateTime modified) =>
        new(name, $@"C:\left\{name}", IsDirectory: false, size, modified);

    private static FolderItem Folder(string name) =>
        new(name, $@"C:\left\{name}", IsDirectory: true, Size: null, ModifiedUtc: DateTime.UnixEpoch);

    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnlyOnOneSideIsOnlyHere()
    {
        var left = new[] { File("a.txt", 10, Stamp) };
        var right = Array.Empty<FolderItem>();

        var (leftResult, rightResult) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.OnlyHere, Assert.Single(leftResult).Status);
        Assert.Empty(rightResult);
    }

    [Fact]
    public void SameSizeAndModifiedIsSame()
    {
        var left = new[] { File("a.txt", 10, Stamp) };
        var right = new[] { File("a.txt", 10, Stamp) };

        var (leftResult, rightResult) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Same, Assert.Single(leftResult).Status);
        Assert.Equal(CompareStatus.Same, Assert.Single(rightResult).Status);
    }

    [Fact]
    public void DifferentSizeIsDifferent()
    {
        var left = new[] { File("a.txt", 10, Stamp) };
        var right = new[] { File("a.txt", 11, Stamp) };

        var (leftResult, rightResult) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Different, Assert.Single(leftResult).Status);
        Assert.Equal(CompareStatus.Different, Assert.Single(rightResult).Status);
    }

    [Fact]
    public void DifferentModifiedIsDifferent()
    {
        var left = new[] { File("a.txt", 10, Stamp) };
        var right = new[] { File("a.txt", 10, Stamp.AddSeconds(1)) };

        var (leftResult, _) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Different, Assert.Single(leftResult).Status);
    }

    [Fact]
    public void NameMatchIsCaseInsensitive()
    {
        var left = new[] { File("A.txt", 10, Stamp) };
        var right = new[] { File("a.TXT", 10, Stamp) };

        var (leftResult, _) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Same, Assert.Single(leftResult).Status);
    }

    [Fact]
    public void SameNameFolderOnBothSidesIsSameWithoutRecursing()
    {
        var left = new[] { Folder("sub") };
        var right = new[] { Folder("sub") };

        var (leftResult, _) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Same, Assert.Single(leftResult).Status);
    }

    [Fact]
    public void FolderVersusFileWithSameNameIsDifferent()
    {
        var left = new[] { Folder("thing") };
        var right = new[] { File("thing", 10, Stamp) };

        var (leftResult, rightResult) = TileComparer.Compare(left, right);

        Assert.Equal(CompareStatus.Different, Assert.Single(leftResult).Status);
        Assert.Equal(CompareStatus.Different, Assert.Single(rightResult).Status);
    }
}
