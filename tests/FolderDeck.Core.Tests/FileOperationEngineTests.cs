using System.Diagnostics;
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

    /// <summary>
    /// 목적지가 여럿인 이동은 첫 목적지에서 원본이 사라지고 나머지가 실패한다. 엔진은
    /// 시키는 대로 할 뿐이고, <b>그런 요청을 만들지 않는 것은 진입점의 몫</b>이다 —
    /// UI 는 목적지 2개 이상에서의 이동을 아예 거부한다(§4.2).
    /// </summary>
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

    // ── 제자리 복제 (Phase 8-D ②) ────────────────────────────────────────────
    //
    // 위 판이 못박은 "원본과 목적지가 같으면 건너뛴다"에 <b>구멍 하나</b>가 났다:
    // 복사이면서 「둘 다 두기」일 때만이다. 그때는 할 일이 있다 — 옆에 사본을 둔다.

    /// <summary>
    /// 같은 폴더로 복사 + <see cref="ConflictPolicy.Rename"/> = 제자리 복제.
    /// 새 이름 형식은 <b>이미 있던 것</b>이다 — 다른 폴더에서 겹칠 때 쓰던 그 형식.
    /// </summary>
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

    /// <summary>폴더도 같다. 새 이름은 원본 바깥이라 재귀 복사가 자기 꼬리를 먹지 않는다.</summary>
    [Fact]
    public async Task CopyingAFolderOntoItselfWithRenameMakesACopy()
    {
        var src = Dir("src");
        var sub = Dir("src/sub");
        File_("src/sub/a.txt");

        var report = await Run(FileOperationKind.Copy, [sub], [src], ConflictPolicy.Rename);

        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(report.Results).Status);
        Assert.Equal("x", File.ReadAllText(Path.Combine(src, "sub (2)", "a.txt")));

        // 복제본 안에 또 복제본이 생기지 않았다.
        Assert.False(Directory.Exists(Path.Combine(src, "sub (2)", "sub (2)")));
        Assert.Equal(["a.txt"], Directory.GetFiles(sub).Select(Path.GetFileName).Order());
    }

    /// <summary>
    /// <b>이동은 이 구멍에 들지 않는다.</b> 같은 폴더로 옮기면서 이름만 바꾸는 것은 이동이
    /// 아니라 이름 바꾸기다 — 정책이 「둘 다 두기」여도 건너뛴다.
    /// </summary>
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

    /// <summary>덮어쓰기·건너뛰기는 지금 그대로다 — 구멍은 「둘 다 두기」 하나뿐이다.</summary>
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


    // ── 볼륨을 건너뛰는 이동 (§1.1 — 실질적으로 복사 + 삭제) ────────────────

    /// <summary>
    /// 다른 볼륨으로의 폴더 이동은 <c>Directory.Move</c> 가 못 하므로 복사 후 삭제로 풀린다.
    /// <b>삭제는 복사가 전부 끝난 뒤에만</b> 일어난다.
    /// </summary>
    [Fact]
    public async Task AFolderMoveAcrossVolumesCopiesThenDeletesTheOriginal()
    {
        // 볼륨이 하나뿐인 기기에서는 시험할 대상 자체가 없다 (xunit 2.x 에는 동적 건너뜀이
        // 없어 조용히 통과한다 — 어느 기기에서 실제로 돌았는지는 보고에 남긴다).
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

    /// <summary>
    /// 볼륨 간 이동을 중간에 취소해도 <b>원본은 통째로 남는다</b>. 복사가 다 끝나야 삭제
    /// 단계로 가므로, 취소는 원본을 반쯤 지운 상태를 만들 수 없다.
    /// </summary>
    [Fact]
    public async Task CancelingAFolderMoveAcrossVolumesLeavesTheWholeOriginal()
    {
        // 볼륨이 하나뿐인 기기에서는 시험할 대상 자체가 없다 (xunit 2.x 에는 동적 건너뜀이
        // 없어 조용히 통과한다 — 어느 기기에서 실제로 돌았는지는 보고에 남긴다).
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

            // 취소가 절반 처리 상태를 숨기지 않는다 — 목적지에 일부가 남았다고 적는다.
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

    /// <summary>
    /// 임시 폴더(대개 <c>C:</c>)와 다른 볼륨에 있는 쓰기 가능한 폴더. 테스트 출력 폴더를 쓴다 —
    /// 사용자 디스크에 새 자리를 만들지 않기 위해서다. 같은 볼륨이면 null.
    /// </summary>
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
            // 정리 실패는 무시.
        }
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

    // ── 정션/링크 순환 방어 (검토 회차 발견) ────────────────────────────────

    /// <summary>
    /// 정션/디렉터리 심볼릭 링크가 자기 조상을 가리키면, 안으로 그대로 재귀할 때
    /// <see cref="StackOverflowException"/>(catch 로 못 잡고 프로세스가 즉시 종료된다)로
    /// 이어질 수 있었다. 링크는 복사하지 않고 건너뛴 사실만 남겨야 한다.
    /// </summary>
    [Fact]
    public async Task CopyingAFolderContainingASelfReferencingLinkDoesNotRecurseForever()
    {
        var tree = Dir("tree");
        File_("tree/a.txt", "A");
        var link = Path.Combine(tree, "loop");

        if (!TryCreateJunction(link, tree))
        {
            // 이 환경에서 정션을 못 만든다 — 시험할 대상 자체가 없다. 다른 볼륨 시험
            // (OtherVolumeDir)과 같은 결의 환경 의존이다.
            return;
        }

        try
        {
            var one = Dir("one");
            var report = await Run(FileOperationKind.Copy, [tree], [one]);

            // 진짜 파일은 그대로 복사됐다.
            Assert.Equal("A", File.ReadAllText(Path.Combine(one, "tree", "a.txt")));

            // 링크 안으로는 들어가지 않았다 — 들어갔으면 무한 재귀로 여기 닿지 못했을 것이다.
            Assert.False(Directory.Exists(Path.Combine(one, "tree", "loop", "a.txt")));

            // 건너뛴 사실은 사유에 남는다 — 조용히 사라지지 않는다(§5).
            var result = Assert.Single(report.Results);
            Assert.Equal(OperationItemStatus.Failed, result.Status);
            Assert.Contains("정션/링크라 건너뛰었다", result.Reason);
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    /// <summary>
    /// Windows 정션을 만든다. <c>Directory.CreateSymbolicLink</c> 와 달리 특별한 권한이나
    /// 개발자 모드가 필요 없다(이 환경에서 <c>mklink /J</c> 로 실측 확인) — 그래서 검토
    /// 회차의 순환 방어 시험은 정션으로 짠다.
    /// </summary>
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
