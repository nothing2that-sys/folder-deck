using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.Core.Tests;

/// <summary>휴지통 대신 무엇을 보냈는지만 적는다 — 테스트가 진짜 휴지통을 건드리지 않게.</summary>
internal sealed class FakeRecycleBin : IRecycleBin
{
    public List<string> Sent { get; } = [];

    /// <summary>경로별로 심어 둔 실패 사유. 없으면 성공.</summary>
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

/// <summary>단일 실행 엔진 (§4.2 · §4.3 · §5 · Phase 3 Stage 1).</summary>
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
            // 정리 실패는 무시.
        }
    }

    // ── 만들기 헬퍼 ──────────────────────────────────────────────────────────

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

    // ── 다중 × 다중 복사 ─────────────────────────────────────────────────────

    [Fact]
    public async Task CopyRunsTheCartesianProductOfSourcesAndDestinations()
    {
        var a = File_("src/a.txt", "A");
        var b = File_("src/b.txt", "B");
        var one = Dir("one");
        var two = Dir("two");

        var report = await Run(FileOperationKind.Copy, [a, b], [one, two]);

        // 파일 2개 × 폴더 2개 = 4건.
        Assert.Equal(4, report.Results.Count);
        Assert.All(report.Results, r => Assert.Equal(OperationItemStatus.Succeeded, r.Status));
        Assert.False(report.Canceled);

        Assert.Equal("A", File.ReadAllText(Path.Combine(one, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(two, "b.txt")));

        // 복사이므로 원본은 그대로.
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

    /// <summary>목적지가 여럿인 이동은 첫 목적지에서 원본이 사라진다 — 그래서 UI 가 확인을 받는다.</summary>
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

    // ── 덮어쓰기 (§4.3) ──────────────────────────────────────────────────────

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

    // ── 부분 실패 (§5) ───────────────────────────────────────────────────────

    [Fact]
    public async Task OneBadItemDoesNotStopTheRest()
    {
        var good = File_("src/good.txt", "G");
        var missing = Path.Combine(_root, "src", "gone.txt");
        var locked = File_("src/locked.txt", "L");
        var one = Dir("one");

        // 이미 있는 이름 + 건너뛰기 = 건너뜀 한 건.
        File.WriteAllText(Path.Combine(one, "locked.txt"), "old");

        var report = await Run(
            FileOperationKind.Copy, [good, missing, locked], [one], ConflictPolicy.Skip);

        Assert.Equal(3, report.Results.Count);
        Assert.Equal(OperationItemStatus.Succeeded, report.Results[0].Status);
        Assert.Equal(OperationItemStatus.Failed, report.Results[1].Status);
        Assert.Equal("원본이 없다", report.Results[1].Reason);
        Assert.Equal(OperationItemStatus.Skipped, report.Results[2].Status);

        // 성공한 것은 실제로 복사됐다 — 실패가 전체를 되돌리지 않는다.
        Assert.True(File.Exists(Path.Combine(one, "good.txt")));
    }

    [Fact]
    public async Task AFileHeldOpenFailsWithAReadableReason()
    {
        var a = File_("src/a.txt", "A");
        var one = Dir("one");
        var target = Path.Combine(one, "a.txt");
        File.WriteAllText(target, "old");

        // 목적지 파일을 붙잡아 둔다 — 설비가 로그를 쓰고 있는 상황과 같다.
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

    // ── 휴지통 (§4.4) ────────────────────────────────────────────────────────

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

    // ── 폴더 재귀 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyingAFolderIsRecursiveAndReportsOneResult()
    {
        File_("src/tree/a.txt", "A");
        File_("src/tree/inner/b.txt", "B");
        File_("src/tree/inner/deep/c.txt", "C");
        var tree = Path.Combine(_root, "src", "tree");
        var one = Dir("one");

        var report = await Run(FileOperationKind.Copy, [tree], [one]);

        // 안에 파일이 셋이어도 사용자가 시킨 것은 한 건이다.
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

    /// <summary>합칠 때 개별 파일 충돌은 덮어쓰기 규칙 그대로 (§4.3).</summary>
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

        // 합치기지 갈아치우기가 아니다 — 기존 것은 남는다.
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

            // 다 옮기지 못했으면 원본을 지우지 않는다.
            Assert.True(Directory.Exists(tree));
            Assert.Equal("OK", File.ReadAllText(Path.Combine(one, "tree", "ok.txt")));
        }
    }

    // ── 취소 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelStopsBetweenUnitsAndReportsWhatWasDone()
    {
        var files = Enumerable.Range(0, 5).Select(i => File_($"src/f{i}.txt", $"{i}")).ToList();
        var one = Dir("one");

        using var cts = new CancellationTokenSource();
        var seen = 0;
        var progress = new Progress<FileOperationProgress>(_ => { });

        // 두 건이 끝나면 취소한다.
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

        // 손댄 것만 결과에 있다. 시작도 안 한 것은 없다.
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

        // 합칠 것이 있으면 빠른 이름바꾸기 대신 트리를 걷는다 — 취소가 끼어들 수 있는 경로다.
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

        // 이동이 끝나지 않았으므로 원본은 통째로 남아 있다 — 반쯤 사라지지 않는다.
        Assert.True(Directory.Exists(tree));
        Assert.Equal(40, Directory.GetFiles(tree).Length);
    }


    /// <summary>
    /// 같은 볼륨 + 합칠 것 없음 = 이름만 바꾼다. 트리를 걷지 않으므로 파일 수와 무관하게 한 번에 끝난다
    /// (그래서 이 경우엔 중간 취소도 없다). 볼륨이 다르면 Directory.Move 가 못 하므로 복사 후 삭제로 간다.
    /// </summary>
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

        // 20개를 하나씩 만지지 않았다 — 폴더 자체를 한 번 건드렸을 뿐이다.
        Assert.Equal(1, reports[^1].CompletedEntries);
    }

    // ── 진행 보고 ────────────────────────────────────────────────────────────

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

        // 폴더 안 2개 + 낱개 1개.
        Assert.Equal(3, reports[^1].CompletedEntries);
    }

    // ── 미리보기 (§4.7) ──────────────────────────────────────────────────────

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

        // 파일 2개 × 살아 있는 폴더 2개 = 4건, 그중 덮어쓸 것 1건.
        Assert.Equal(4, preview.TotalItemCount);
        Assert.Equal(1, preview.TotalOverwriteCount);
        Assert.Equal(2, preview.PerDestination.Count);
        Assert.Equal(1, preview.PerDestination.Single(d => d.Destination == one).OverwriteCount);
        Assert.Equal(0, preview.PerDestination.Single(d => d.Destination == two).OverwriteCount);
        Assert.Equal(gone, Assert.Single(preview.SkippedDestinations));
    }

    /// <summary>건너뛰기·이름바꾸기는 덮어쓰지 않으므로 "덮어쓸 건수"가 0 이다.</summary>
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

    /// <summary><see cref="Progress{T}"/> 는 동기화 컨텍스트로 던져 순서가 흐트러진다 — 바로 부른다.</summary>
    private sealed class DelegateProgress(Action<FileOperationProgress> onReport)
        : IProgress<FileOperationProgress>
    {
        public void Report(FileOperationProgress value) => onReport(value);
    }
}
