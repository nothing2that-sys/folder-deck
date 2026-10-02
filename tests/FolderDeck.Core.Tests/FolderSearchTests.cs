using System.Diagnostics;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Tests;

public sealed class FolderSearchTests : IDisposable
{
    private readonly string _root;
    private readonly FolderEnumerator _enumerator = new();

    public FolderSearchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.SearchTests", Guid.NewGuid().ToString("N"));
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

    private void Write(params string[] relativeParts)
    {
        var path = Path.Combine([_root, .. relativeParts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private async Task<List<FolderSearchHit>> Search(
        string query, FolderSearchStats? stats = null, string? root = null)
    {
        var hits = new List<FolderSearchHit>();
        await foreach (var hit in _enumerator.SearchAsync(
            root ?? _root, query, stats ?? new FolderSearchStats()))
        {
            hits.Add(hit);
        }

        return hits;
    }

    [Fact]
    public async Task FindsMatchesScatteredAcrossDepths()
    {
        Write("Recipe", "Controls", "NumberBox.xaml");
        Write("Views", "NumberBoxHost.cs");
        Write("Recipe", "NumberFormat.cs");
        Write("Untouched.txt");

        var hits = await Search("number");

        Assert.Equal(
            ["NumberBox.xaml", "NumberBoxHost.cs", "NumberFormat.cs"],
            hits.Select(h => h.Item.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReportsRelativeFolderWithoutTheSearchBase()
    {
        Write("Recipe", "Controls", "NumberBox.xaml");
        Write("Views", "NumberBoxHost.cs");
        Write("Top.txt");

        var hits = await Search("o");
        var byName = hits.ToDictionary(h => h.Item.Name, h => h.RelativeFolder);

        Assert.Equal(string.Empty, byName["Top.txt"]);
        Assert.Equal(Path.Combine("Recipe", "Controls"), byName["NumberBox.xaml"]);
        Assert.Equal("Views", byName["NumberBoxHost.cs"]);

        Assert.Equal("Recipe", byName["Controls"]);
    }

    [Fact]
    public async Task SearchBaseDeterminesTheScope()
    {
        Write("Recipe", "Controls", "NumberBox.xaml");
        Write("Views", "NumberBoxHost.cs");

        var fromRoot = await Search("number");
        Assert.Equal(2, fromRoot.Count);

        var fromRecipe = await Search("number", root: Path.Combine(_root, "Recipe"));
        Assert.Equal("NumberBox.xaml", Assert.Single(fromRecipe).Item.Name);
        Assert.Equal("Controls", fromRecipe[0].RelativeFolder);
    }

    [Fact]
    public async Task MatchesArePartialAndCaseInsensitive()
    {
        Write("MiXeDcAsE.TXT");

        Assert.Single(await Search("mixedcase"));
        Assert.Single(await Search("XEDCA"));
        Assert.Empty(await Search("nothinglikethis"));
    }

    [Fact]
    public async Task FindsFoldersNotJustFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "TargetFolder"));

        var hit = Assert.Single(await Search("targetfolder"));

        Assert.True(hit.Item.IsDirectory);
        Assert.Null(hit.Item.Size);
    }

    [Fact]
    public async Task ASelfReferencingLinkDoesNotMakeTheSearchRunForever()
    {
        Write("tree", "loop-marker.txt");
        var tree = Path.Combine(_root, "tree");
        var link = Path.Combine(tree, "loop");

        if (!TryCreateJunction(link, tree))
        {

            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var hits = new List<FolderSearchHit>();

            await foreach (var hit in _enumerator.SearchAsync(_root, "loop", new FolderSearchStats(), cts.Token))
            {
                hits.Add(hit);
            }

            Assert.Equal(1, hits.Count(h => h.Item.Name == "loop-marker.txt"));
            Assert.Equal(1, hits.Count(h => h.Item.Name == "loop"));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        var info = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(info);
        process?.WaitForExit();

        return process?.ExitCode == 0 && Directory.Exists(linkPath);
    }

    [Fact]
    public async Task CountsScannedFolders()
    {
        Write("a", "b", "deep.txt");
        var stats = new FolderSearchStats();

        await Search("deep", stats);

        Assert.Equal(3, stats.FoldersScanned);
        Assert.Equal(0, stats.FoldersSkipped);
    }

    [Fact]
    public async Task SkipsUnreachableSubtreesAndKeepsGoing()
    {
        Write("reachable", "found-me.txt");

        var stats = new FolderSearchStats();
        var hits = await Search("anything", stats, root: @"\\folderdeck-no-such-host\logs");

        Assert.Empty(hits);
        Assert.Equal(1, stats.FoldersSkipped);
        Assert.Equal(0, stats.FoldersScanned);

        var ok = new FolderSearchStats();
        Assert.Single(await Search("found-me", ok));
        Assert.Equal(0, ok.FoldersSkipped);
    }

    [Fact]
    public async Task MissingRootIsSkippedNotThrown()
    {
        var stats = new FolderSearchStats();

        var hits = await Search("x", stats, root: Path.Combine(_root, "없는-폴더"));

        Assert.Empty(hits);
        Assert.Equal(1, stats.FoldersSkipped);
    }

    [Fact]
    public async Task CancellationStopsTheWalkPartWayThrough()
    {
        for (var i = 0; i < 400; i++)
        {
            Write($"dir{i:D3}", $"match{i:D3}.txt");
        }

        using var cts = new CancellationTokenSource();
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _enumerator.SearchAsync(_root, "match", new FolderSearchStats(), cts.Token))
            {
                seen++;
                if (seen == 5)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.InRange(seen, 5, 200);
    }

    [Fact]
    public async Task AlreadyCancelledSearchYieldsNothing()
    {
        Write("match.txt");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _enumerator.SearchAsync(_root, "match", new FolderSearchStats(), cts.Token))
            {
                Assert.Fail("취소된 검색이 결과를 냈다.");
            }
        });
    }

    [Fact]
    public async Task StreamsResultsBeforeTheWalkFinishes()
    {
        for (var i = 0; i < 300; i++)
        {
            Write($"dir{i:D3}", $"match{i:D3}.txt");
        }

        var stats = new FolderSearchStats();
        using var cts = new CancellationTokenSource();

        await using var results = _enumerator
            .SearchAsync(_root, "match", stats, cts.Token)
            .GetAsyncEnumerator(cts.Token);

        Assert.True(await results.MoveNextAsync());

        Assert.InRange(stats.FoldersScanned, 1, 300);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task DeepTreeDoesNotOverflowTheStack()
    {
        var path = _root;
        for (var i = 0; i < 120; i++)
        {
            path = Path.Combine(path, $"d{i}");
        }

        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "bottom.txt"), "x");

        Assert.Single(await Search("bottom"));
    }
}
