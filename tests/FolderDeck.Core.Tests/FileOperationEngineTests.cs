using System.Diagnostics;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.Core.Tests;

internal sealed class FakeRecycleBin : IRecycleBin
{
    public List<string> Sent { get; } = [];

    public Dictionary<string, string> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Send(string path)
    {
        if (Failures.TryGetValue(path, out var reason))
        {
            return reason;
        }

        Sent.Add(path);
        File.Delete(path);
        return null;
    }
}

public sealed class FileOperationEngineTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRecycleBin _bin = new();
    private readonly FileOperationEngine _engine;

    public FileOperationEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.OpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _engine = new FileOperationEngine(_bin);
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

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static OperationItem Item(string path) => new(path, Directory.Exists(path));

    private Task<FileOperationReport> Run(
        FileOperationKind op,
        IEnumerable<string> sources,
        IEnumerable<string> destinations,
        ConflictPolicy onConflict = ConflictPolicy.Overwrite,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken token = default)
        => _engine.ExecuteAsync(
            new FileOperationRequest(op, [.. sources.Select(Item)], [.. destinations], onConflict),
            progress,
            token);

    [Fact]
    public async Task CopyRunsTheCartesianProductOfSourcesAndDestinations()
    {
        var a = File_("src/a.txt", "A");
        var b = File_("src/b.txt", "B");
        var one = Dir("one");
        var two = Dir("two");

        var report = await Run(FileOperationKind.Copy, [a, b], [one, two]);

        Assert.Equal(4, report.Results.Count);
        Assert.All(report.Results, r => Assert.Equal(OperationItemStatus.Succeeded, r.Status));
        Assert.False(report.Canceled);

        Assert.Equal("A", File.ReadAllText(Path.Combine(one, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(two, "b.txt")));

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task MoveLeavesNoOriginalBehind()
    {
        var a = File_("src/a.txt", "A");
        var one = Dir("one");

        var report = await Run(FileOperationKind.Move, [a], [one]);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.False(File.Exists(a));
        Assert.Equal("A", File.ReadAllText(Path.Combine(one, "a.txt")));
    }

    [Fact]
    public async Task MoveToTwoDestinationsSucceedsOnceAndReportsTheRest()
    {
        var a = File_("src/a.txt", "A");
        var one = Dir("one");
        var two = Dir("two");

        var report = await Run(FileOperationKind.Move, [a], [one, two]);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(OperationItemStatus.Succeeded, report.Results[0].Status);
        Assert.Equal(OperationItemStatus.Failed, report.Results[1].Status);
        Assert.Equal("원본이 없다", report.Results[1].Reason);
    }

    [Fact]
    public async Task OverwriteIsTheDefaultAndIsRecordedNotBlocked()
    {
        var a = File_("src/a.txt", "새 내용");
        var one = Dir("one");
        File.WriteAllText(Path.Combine(one, "a.txt"), "옛 내용");

        var report = await Run(FileOperationKind.Copy, [a], [one]);

        var result = Assert.Single(report.Results);
        Assert.Equal(OperationItemStatus.Succeeded, result.Status);
        Assert.True(result.Overwritten);
        Assert.Equal("새 내용", File.ReadAllText(Path.Combine(one, "a.txt")));
    }

    [Fact]
    public async Task SkipPolicyLeavesTheExistingFileAlone()
    {
        var a = File_("src/a.txt", "새 내용");
        var one = Dir("one");
        File.WriteAllText(Path.Combine(one, "a.txt"), "옛 내용");

        var report = await Run(FileOperationKind.Copy, [a], [one], ConflictPolicy.Skip);

        var result = Assert.Single(report.Results);
        Assert.Equal(OperationItemStatus.Skipped, result.Status);
        Assert.Equal("같은 이름이 이미 있다", result.Reason);
        Assert.False(result.Overwritten);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(one, "a.txt")));
    }

    [Fact]
    public async Task RenamePolicyKeepsBoth()
    {
        var a = File_("src/a.txt", "새 내용");
        var one = Dir("one");
        File.WriteAllText(Path.Combine(one, "a.txt"), "옛 내용");

        var report = await Run(FileOperationKind.Copy, [a], [one], ConflictPolicy.Rename);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.False(report.Results[0].Overwritten);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(one, "a.txt")));
        Assert.Equal("새 내용", File.ReadAllText(Path.Combine(one, "a (2).txt")));
    }

    [Fact]
    public async Task OneBadItemDoesNotStopTheRest()
    {
        var good = File_("src/good.txt", "G");
        var missing = Path.Combine(_root, "src", "gone.txt");
        var locked = File_("src/locked.txt", "L");
        var one = Dir("one");

        File.WriteAllText(Path.Combine(one, "locked.txt"), "old");

        var report = await Run(
            FileOperationKind.Copy, [good, missing, locked], [one], ConflictPolicy.Skip);

        Assert.Equal(3, report.Results.Count);
        Assert.Equal(OperationItemStatus.Succeeded, report.Results[0].Status);
        Assert.Equal(OperationItemStatus.Failed, report.Results[1].Status);
        Assert.Equal("원본이 없다", report.Results[1].Reason);
        Assert.Equal(OperationItemStatus.Skipped, report.Results[2].Status);

        Assert.True(File.Exists(Path.Combine(one, "good.txt")));
    }

    [Fact]
    public async Task AFileHeldOpenFailsWithAReadableReason()
    {
        var a = File_("src/a.txt", "A");
        var one = Dir("one");
        var target = Path.Combine(one, "a.txt");
        File.WriteAllText(target, "old");

        using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = await Run(FileOperationKind.Copy, [a], [one]);

            var result = Assert.Single(report.Results);
            Assert.Equal(OperationItemStatus.Failed, result.Status);
            Assert.Equal("다른 프로그램이 쓰고 있다", result.Reason);
        }
    }

    [Fact]
    public async Task AnUnreachableDestinationIsSkippedNotFatal()
    {
        var a = File_("src/a.txt");
        var one = Dir("one");
        var gone = Path.Combine(_root, "no-such-folder");

        var report = await Run(FileOperationKind.Copy, [a], [gone, one]);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(OperationItemStatus.Skipped, report.Results[0].Status);
        Assert.Equal("목적지에 닿을 수 없다", report.Results[0].Reason);
        Assert.Equal(OperationItemStatus.Succeeded, report.Results[1].Status);
    }

    [Fact]
    public async Task CopyingAFileOntoItselfIsSkipped()
    {
        var src = Dir("src");
        var a = File_("src/a.txt");

        var report = await Run(FileOperationKind.Copy, [a], [src]);

        Assert.Equal(OperationItemStatus.Skipped, Assert.Single(report.Results).Status);
        Assert.Equal("원본과 목적지가 같다", report.Results[0].Reason);
        Assert.Equal("x", File.ReadAllText(a));
    }

    [Fact]
    public async Task CopyingAFileOntoItselfWithRenameMakesACopy()
    {
        var src = Dir("src");
        var a = File_("src/a.txt");

        var report = await Run(FileOperationKind.Copy, [a], [src], ConflictPolicy.Rename);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.Equal("x", File.ReadAllText(a));
        Assert.Equal("x", File.ReadAllText(Path.Combine(src, "a (2).txt")));
    }

    [Fact]
    public async Task CopyingAFolderOntoItselfWithRenameMakesACopy()
    {
        var src = Dir("src");
        var sub = Dir("src/sub");
        File_("src/sub/a.txt");

        var report = await Run(FileOperationKind.Copy, [sub], [src], ConflictPolicy.Rename);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.Equal("x", File.ReadAllText(Path.Combine(src, "sub (2)", "a.txt")));

        Assert.False(Directory.Exists(Path.Combine(src, "sub (2)", "sub (2)")));
        Assert.Equal(["a.txt"], Directory.GetFiles(sub).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task MovingAFileOntoItselfWithRenameIsStillSkipped()
    {
        var src = Dir("src");
        var a = File_("src/a.txt");

        var report = await Run(FileOperationKind.Move, [a], [src], ConflictPolicy.Rename);

        Assert.Equal(OperationItemStatus.Skipped, Assert.Single(report.Results).Status);
        Assert.Equal("원본과 목적지가 같다", report.Results[0].Reason);
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(Path.Combine(src, "a (2).txt")));
    }

    [Theory]
    [InlineData(ConflictPolicy.Overwrite)]
    [InlineData(ConflictPolicy.Skip)]
    public async Task CopyingOntoItselfStaysSkippedForTheOtherPolicies(ConflictPolicy policy)
    {
        var src = Dir("src");
        var a = File_("src/a.txt");

        var report = await Run(FileOperationKind.Copy, [a], [src], policy);

        Assert.Equal(OperationItemStatus.Skipped, Assert.Single(report.Results).Status);
        Assert.Single(Directory.GetFiles(src));
        Assert.Equal("x", File.ReadAllText(a));
    }

    [Fact]
    public async Task TrashTakesSourcesOnlyAndUsesTheRecycleBin()
    {
        var a = File_("src/a.txt");
        var b = File_("src/b.txt");

        var report = await Run(FileOperationKind.Trash, [a, b], []);

        Assert.Equal(2, report.Results.Count);
        Assert.All(report.Results, r => Assert.Equal(OperationItemStatus.Succeeded, r.Status));
        Assert.Equal([a, b], _bin.Sent);
        Assert.False(File.Exists(a));
    }

    [Fact]
    public async Task TrashFailureIsReportedPerItem()
    {
        var a = File_("src/a.txt");
        var b = File_("src/b.txt");
        _bin.Failures[a] = "다른 프로그램이 쓰고 있다";

        var report = await Run(FileOperationKind.Trash, [a, b], []);

        Assert.Equal(OperationItemStatus.Failed, report.Results[0].Status);
        Assert.Equal("다른 프로그램이 쓰고 있다", report.Results[0].Reason);
        Assert.Equal(OperationItemStatus.Succeeded, report.Results[1].Status);
    }

    [Fact]
    public async Task CopyingAFolderIsRecursiveAndReportsOneResult()
    {
        File_("src/tree/a.txt", "A");
        File_("src/tree/inner/b.txt", "B");
        File_("src/tree/inner/deep/c.txt", "C");
        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");

        var report = await Run(FileOperationKind.Copy, [tree], [one]);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.Equal("C", File.ReadAllText(Path.Combine(one, "tree", "inner", "deep", "c.txt")));
        Assert.True(Directory.Exists(tree));
    }

    [Fact]
    public async Task MovingAFolderRemovesTheOriginalTree()
    {
        File_("src/tree/a.txt", "A");
        File_("src/tree/inner/b.txt", "B");
        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");

        var report = await Run(FileOperationKind.Move, [tree], [one]);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.False(Directory.Exists(tree));
        Assert.Equal("B", File.ReadAllText(Path.Combine(one, "tree", "inner", "b.txt")));
    }

    [Fact]
    public async Task MergingFoldersOverwritesPerFileAndFlagsIt()
    {
        File_("src/tree/a.txt", "새 A");
        File_("src/tree/only-new.txt", "N");
        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");
        File_("one/tree/a.txt", "옛 A");
        File_("one/tree/only-old.txt", "O");

        var report = await Run(FileOperationKind.Copy, [tree], [one]);

        var result = Assert.Single(report.Results);
        Assert.Equal(OperationItemStatus.Succeeded, result.Status);
        Assert.True(result.Overwritten);

        Assert.Equal("새 A", File.ReadAllText(Path.Combine(one, "tree", "a.txt")));
        Assert.Equal("N", File.ReadAllText(Path.Combine(one, "tree", "only-new.txt")));

        Assert.Equal("O", File.ReadAllText(Path.Combine(one, "tree", "only-old.txt")));
    }

    [Fact]
    public async Task AFolderCannotBeMovedIntoItself()
    {
        File_("src/tree/a.txt");
        var tree = Path.Combine(_root, "src", "tree");
        var inner = Directory.CreateDirectory(Path.Combine(tree, "inner")).FullName;

        var report = await Run(FileOperationKind.Move, [tree], [inner]);

        Assert.Equal(OperationItemStatus.Skipped, Assert.Single(report.Results).Status);
        Assert.Equal("폴더를 자기 안으로는 넣을 수 없다", report.Results[0].Reason);
        Assert.True(Directory.Exists(tree));
    }

    [Fact]
    public async Task AFailureInsideAFolderMakesThatUnitFailButKeepsTheOriginal()
    {
        File_("src/tree/ok.txt", "OK");
        File_("src/tree/held.txt", "H");
        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");
        File_("one/tree/held.txt", "old");

        using (File.Open(Path.Combine(one, "tree", "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = await Run(FileOperationKind.Move, [tree], [one]);

            var result = Assert.Single(report.Results);
            Assert.Equal(OperationItemStatus.Failed, result.Status);
            Assert.Contains("1개 항목 실패", result.Reason);
            Assert.Contains("다른 프로그램이 쓰고 있다", result.Reason);

            Assert.True(Directory.Exists(tree));
            Assert.Equal("OK", File.ReadAllText(Path.Combine(one, "tree", "ok.txt")));
        }
    }

    [Fact]
    public async Task CancelStopsBetweenUnitsAndReportsWhatWasDone()
    {
        var files = Enumerable.Range(0, 5).Select(i => File_($"src/f{i}.txt", $"{i}")).ToList();
        var one = Dir("one");

        using var cts = new CancellationTokenSource();
        var seen = 0;
        var progress = new Progress<FileOperationProgress>(_ => { });

        var counting = new DelegateProgress(p =>
        {
            if (p.CompletedItems >= 2 && seen++ == 0)
            {
                cts.Cancel();
            }
        });

        var report = await Run(
            FileOperationKind.Copy, files, [one], progress: counting, token: cts.Token);

        Assert.True(report.Canceled);

        Assert.InRange(report.Results.Count, 2, 4);
        Assert.All(report.Results, r => Assert.Equal(OperationItemStatus.Succeeded, r.Status));
        Assert.True(report.Results.Count < files.Count);

        _ = progress;
    }

    [Fact]
    public async Task CancelingMidFolderLeavesTheSourceAloneAndSaysHowFarItGot()
    {
        for (var i = 0; i < 40; i++)
        {
            File_($"src/tree/f{i}.txt", $"{i}");
        }

        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");

        File_("one/tree/f0.txt", "old");

        using var cts = new CancellationTokenSource();
        var counting = new DelegateProgress(p =>
        {
            if (p.CompletedEntries >= 5)
            {
                cts.Cancel();
            }
        });

        var report = await Run(
            FileOperationKind.Move, [tree], [one], progress: counting, token: cts.Token);

        var result = Assert.Single(report.Results);
        Assert.Equal(OperationItemStatus.Failed, result.Status);
        Assert.Contains("취소", result.Reason);

        Assert.True(Directory.Exists(tree));
        Assert.Equal(40, Directory.GetFiles(tree).Length);
    }

    [Fact]
    public async Task AFolderMoveAcrossVolumesCopiesThenDeletesTheOriginal()
    {

        if (OtherVolumeDir() is not { } other)
        {
            return;
        }

        try
        {
            File_("src/tree/a.txt", "A");
            File_("src/tree/inner/b.txt", "B");
            var tree = Path.Combine(_root, "src", "tree");

            var report = await Run(FileOperationKind.Move, [tree], [other]);

            Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
            Assert.False(Directory.Exists(tree));
            Assert.Equal("B", File.ReadAllText(Path.Combine(other, "tree", "inner", "b.txt")));
        }
        finally
        {
            Discard(other);
        }
    }

    [Fact]
    public async Task CancelingAFolderMoveAcrossVolumesLeavesTheWholeOriginal()
    {

        if (OtherVolumeDir() is not { } other)
        {
            return;
        }

        try
        {
            for (var i = 0; i < 40; i++)
            {
                File_($"src/tree/f{i}.txt", $"{i}");
            }

            var tree = Path.Combine(_root, "src", "tree");

            using var cts = new CancellationTokenSource();
            var counting = new DelegateProgress(p =>
            {
                if (p.CompletedEntries >= 5)
                {
                    cts.Cancel();
                }
            });

            var report = await Run(
                FileOperationKind.Move, [tree], [other], progress: counting, token: cts.Token);

            var result = Assert.Single(report.Results);
            Assert.Equal(OperationItemStatus.Failed, result.Status);
            Assert.Contains("취소", result.Reason);

            Assert.Contains("목적지에 일부가 남았다", result.Reason);
            Assert.Contains("원본은 그대로", result.Reason);

            Assert.True(Directory.Exists(tree));
            Assert.Equal(40, Directory.GetFiles(tree).Length);
        }
        finally
        {
            Discard(other);
        }
    }

    private static string? OtherVolumeDir()
    {
        var here = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));
        var there = Path.GetPathRoot(Path.GetFullPath(AppContext.BaseDirectory));

        if (string.Equals(here, there, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Directory.CreateDirectory(Path.Combine(
            AppContext.BaseDirectory, "cross-volume-tests", Guid.NewGuid().ToString("N"))).FullName;
    }

    private static void Discard(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {

        }
    }

    [Fact]
    public async Task ASameVolumeFolderMoveIsARenameNotATreeWalk()
    {
        for (var i = 0; i < 20; i++)
        {
            File_($"src/tree/f{i}.txt", $"{i}");
        }

        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");

        var reports = new List<FileOperationProgress>();
        await Run(FileOperationKind.Move, [tree], [one], progress: new DelegateProgress(reports.Add));

        Assert.False(Directory.Exists(tree));
        Assert.Equal(20, Directory.GetFiles(Path.Combine(one, "tree")).Length);

        Assert.Equal(1, reports[^1].CompletedEntries);
    }

    [Fact]
    public async Task CopyingAFolderContainingASelfReferencingLinkDoesNotRecurseForever()
    {
        var tree = Dir("tree");
        File_("tree/a.txt", "A");
        var link = Path.Combine(tree, "loop");

        if (!TryCreateJunction(link, tree))
        {

            return;
        }

        try
        {
            var one = Dir("one");
            var report = await Run(FileOperationKind.Copy, [tree], [one]);

            Assert.Equal("A", File.ReadAllText(Path.Combine(one, "tree", "a.txt")));

            Assert.False(Directory.Exists(Path.Combine(one, "tree", "loop", "a.txt")));

            var result = Assert.Single(report.Results);
            Assert.Equal(OperationItemStatus.Failed, result.Status);
            Assert.Contains("정션/링크라 건너뛰었다", result.Reason);
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
    public async Task ProgressCountsUnitsAndActualFiles()
    {
        File_("src/tree/a.txt");
        File_("src/tree/b.txt");
        var tree = Path.Combine(_root, "src", "tree");
        var loose = File_("src/loose.txt");
        var one = Dir("one");

        var reports = new List<FileOperationProgress>();
        var progress = new DelegateProgress(reports.Add);

        await Run(FileOperationKind.Copy, [tree, loose], [one], progress: progress);

        Assert.NotEmpty(reports);
        Assert.All(reports, p => Assert.Equal(2, p.TotalItems));
        Assert.Equal(2, reports[^1].CompletedItems);

        Assert.Equal(3, reports[^1].CompletedEntries);
    }

    [Fact]
    public async Task PreviewCountsItemsOverwritesAndUnreachableDestinations()
    {
        var a = File_("src/a.txt");
        var b = File_("src/b.txt");
        var one = Dir("one");
        var two = Dir("two");
        var gone = Path.Combine(_root, "no-such-folder");
        File.WriteAllText(Path.Combine(one, "a.txt"), "old");

        var preview = await _engine.PreviewAsync(new FileOperationRequest(
            FileOperationKind.Copy, [Item(a), Item(b)], [one, two, gone], ConflictPolicy.Overwrite));

        Assert.Equal(4, preview.TotalItemCount);
        Assert.Equal(1, preview.TotalOverwriteCount);
        Assert.Equal(2, preview.PerDestination.Count);
        Assert.Equal(1, preview.PerDestination.Single(d => d.Destination == one).OverwriteCount);
        Assert.Equal(0, preview.PerDestination.Single(d => d.Destination == two).OverwriteCount);
        Assert.Equal(gone, Assert.Single(preview.SkippedDestinations));
    }

    [Fact]
    public async Task PreviewReportsNoOverwritesWhenThePolicyDoesNotOverwrite()
    {
        var a = File_("src/a.txt");
        var one = Dir("one");
        File.WriteAllText(Path.Combine(one, "a.txt"), "old");

        foreach (var policy in new[] { ConflictPolicy.Skip, ConflictPolicy.Rename })
        {
            var preview = await _engine.PreviewAsync(new FileOperationRequest(
                FileOperationKind.Copy, [Item(a)], [one], policy));

            Assert.Equal(1, preview.TotalItemCount);
            Assert.Equal(0, preview.TotalOverwriteCount);
        }
    }

    [Fact]
    public async Task PreviewOfTrashHasNoDestinations()
    {
        var a = File_("src/a.txt");
        var b = File_("src/b.txt");

        var preview = await _engine.PreviewAsync(new FileOperationRequest(
            FileOperationKind.Trash, [Item(a), Item(b)], [], ConflictPolicy.Overwrite));

        Assert.Equal(2, preview.TotalItemCount);
        Assert.Equal(0, preview.TotalOverwriteCount);
        Assert.Empty(preview.PerDestination);
        Assert.Empty(preview.SkippedDestinations);
    }

    private sealed class DelegateProgress(Action<FileOperationProgress> onReport)
        : IProgress<FileOperationProgress>
    {
        public void Report(FileOperationProgress value) => onReport(value);
    }
}
