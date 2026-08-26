using FolderDeck.Core.Models;

namespace FolderDeck.Core.Operations;

/// <summary>
/// 단일 실행 엔진 (§4.2 · §4.7 · Phase 3 Stage 1). 대상 집합 × 목적지 집합 × 동작.
/// </summary>
/// <remarks>
/// 세 진입점(대상함 · 드래그앤드롭 · 매크로)이 이 하나를 공유한다. 그래서 UI 를 모르고,
/// 확인창을 띄우지 않으며, <b>시키는 대로 실행하고 결과를 돌려준다</b> — 이동 전 확인 같은
/// 안전장치는 UI 단의 몫이다.
/// <para>
/// <b>부분 실패가 기본이다</b>(§5). 한 항목이 터져도 예외로 전체를 중단하지 않고
/// 항목별 성공/건너뜀/실패+사유를 모아 돌린다. 취소도 예외가 아니라 결과다.
/// </para>
/// <para>
/// 결과는 <b>단위(대상 하나 × 목적지 하나)마다 하나</b>다. 폴더 하나를 복사하면 안에
/// 파일이 만 개여도 결과는 한 줄이다 — 사용자가 시킨 것이 "이 폴더를 저기로" 한 건이라서.
/// 폴더 안에서 일부가 실패하면 그 단위가 실패로 잡히고 사유에 몇 개인지 적힌다.
/// </para>
/// </remarks>
public sealed class FileOperationEngine(IRecycleBin recycleBin) : IFileOperationEngine
{
    private readonly IRecycleBin _recycleBin =
        recycleBin ?? throw new ArgumentNullException(nameof(recycleBin));

    public Task<FileOperationPreview> PreviewAsync(
        FileOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() => Preview(request), CancellationToken.None);
    }

    public Task<FileOperationReport> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 취소는 예외가 아니라 결과다 — Task.Run 에 토큰을 넘겨 시작 전에 던지게 하지 않는다.
        return Task.Run(() => Execute(request, progress, cancellationToken), CancellationToken.None);
    }

    // ── 미리보기 ─────────────────────────────────────────────────────────────

    private static FileOperationPreview Preview(FileOperationRequest request)
    {
        if (request.Op == FileOperationKind.Trash)
        {
            // 휴지통은 목적지가 없다. 덮어쓸 것도 없다.
            return new FileOperationPreview(request.Sources.Count, 0, [], []);
        }

        var perDestination = new List<DestinationPreview>();
        var skipped = new List<string>();

        foreach (var destination in request.Destinations)
        {
            if (!CanReach(destination))
            {
                // 접근 불가 목적지는 실행에서 빠진다. 경로 자체는 지우지 않는다 (§4.7).
                skipped.Add(destination);
                continue;
            }

            // "덮어쓸 건수"는 실제로 덮어쓸 때만 센다 — 건너뛰기/이름바꾸기는 덮어쓰지 않는다.
            var collisions = request.OnConflict == ConflictPolicy.Overwrite
                ? request.Sources.Count(s => Exists(TargetOf(s, destination)))
                : 0;

            perDestination.Add(new DestinationPreview(destination, request.Sources.Count, collisions));
        }

        return new FileOperationPreview(
            perDestination.Sum(d => d.ItemCount),
            perDestination.Sum(d => d.OverwriteCount),
            perDestination,
            skipped);
    }

    // ── 실행 ─────────────────────────────────────────────────────────────────

    private FileOperationReport Execute(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        CancellationToken token)
    {
        var units = BuildUnits(request);
        var results = new List<OperationItemResult>(units.Count);
        var walk = new WalkCounter(progress, units.Count);
        var canceled = false;

        foreach (var (source, destination) in units)
        {
            if (token.IsCancellationRequested)
            {
                // 아직 시작도 안 한 단위는 결과에 넣지 않는다 — 손대지 않았으니까.
                canceled = true;
                break;
            }

            walk.BeginUnit(source.Path);
            var result = RunUnit(request, source, destination, walk, token);
            results.Add(result);
            walk.EndUnit();

            if (token.IsCancellationRequested)
            {
                canceled = true;
            }
        }

        return new FileOperationReport(results, canceled);
    }

    /// <summary>대상 × 목적지의 데카르트 곱. 휴지통은 목적지가 없어 대상만.</summary>
    private static List<(OperationItem Source, string Destination)> BuildUnits(FileOperationRequest request)
    {
        if (request.Op == FileOperationKind.Trash)
        {
            return [.. request.Sources.Select(s => (s, string.Empty))];
        }

        return [.. request.Sources.SelectMany(s => request.Destinations.Select(d => (s, d)))];
    }

    private OperationItemResult RunUnit(
        FileOperationRequest request,
        OperationItem source,
        string destination,
        WalkCounter walk,
        CancellationToken token)
    {
        OperationItemResult Ok(bool overwritten = false) =>
            new(source.Path, destination, OperationItemStatus.Succeeded, overwritten, null);

        OperationItemResult Skip(string reason) =>
            new(source.Path, destination, OperationItemStatus.Skipped, false, reason);

        OperationItemResult Fail(string reason, bool overwritten = false) =>
            new(source.Path, destination, OperationItemStatus.Failed, overwritten, reason);

        try
        {
            if (!Exists(source.Path))
            {
                return Fail("원본이 없다");
            }

            if (request.Op == FileOperationKind.Trash)
            {
                walk.Touch(source.Path);
                var failure = _recycleBin.Send(source.Path);
                return failure is null ? Ok() : Fail(failure);
            }

            if (!CanReach(destination))
            {
                return Skip("목적지에 닿을 수 없다");
            }

            return source.IsDirectory || Directory.Exists(source.Path)
                ? RunFolderUnit(request, source, destination, walk, token, Ok, Skip, Fail)
                : RunFileUnit(request, source, destination, walk, Ok, Skip);
        }
        catch (Exception ex)
        {
            // 한 항목이 터져도 나머지는 계속된다 — 그래서 여기서 결과로 접는다.
            return Fail(Describe(ex));
        }
    }

    private static OperationItemResult RunFileUnit(
        FileOperationRequest request,
        OperationItem source,
        string destination,
        WalkCounter walk,
        Func<bool, OperationItemResult> ok,
        Func<string, OperationItemResult> skip)
    {
        var target = TargetOf(source, destination);

        // 제자리 복제(§4.3 「둘 다 두기」)만 이 관문을 지나간다 — 아래 Rename 가지가
        // 새 이름을 지어 준다. 나머지는 지금 그대로 할 일이 없다.
        if (!IsInPlaceDuplicate(request, source.Path, target) && SamePath(source.Path, target))
        {
            return skip("원본과 목적지가 같다");
        }

        var overwriting = Exists(target);
        switch (request.OnConflict)
        {
            case ConflictPolicy.Skip when overwriting:
                return skip("같은 이름이 이미 있다");

            case ConflictPolicy.Rename when overwriting:
                target = UniqueName(target);
                overwriting = false;
                break;
        }

        walk.Touch(source.Path);

        if (request.Op == FileOperationKind.Move)
        {
            // File.Move 는 볼륨을 건너뛰어도 동작한다(D: → C: 확인). 폴더와 다르다.
            File.Move(source.Path, target, overwrite: true);
        }
        else
        {
            File.Copy(source.Path, target, overwrite: true);
        }

        return ok(overwriting);
    }

    private static OperationItemResult RunFolderUnit(
        FileOperationRequest request,
        OperationItem source,
        string destination,
        WalkCounter walk,
        CancellationToken token,
        Func<bool, OperationItemResult> ok,
        Func<string, OperationItemResult> skip,
        Func<string, bool, OperationItemResult> fail)
    {
        var target = TargetOf(source, destination);

        // 파일 쪽과 같은 관문이다. 지나가면 아래 Rename 가지가 `sub` 를 `sub (2)` 로 만들고,
        // 그 새 이름은 원본 <b>바깥</b>이라 아래 재귀 복사가 자기 꼬리를 먹지 않는다
        // (CopyTree 는 원본 폴더만 훑는다).
        if (!IsInPlaceDuplicate(request, source.Path, target) && SamePath(source.Path, target))
        {
            return skip("원본과 목적지가 같다");
        }

        // 폴더를 자기 안(또는 자기 하위)으로 옮기면 무한히 자란다.
        if (IsUnder(target, source.Path))
        {
            return skip("폴더를 자기 안으로는 넣을 수 없다");
        }

        var merging = Directory.Exists(target);

        if (request.OnConflict == ConflictPolicy.Skip && merging)
        {
            return skip("같은 이름이 이미 있다");
        }

        if (request.OnConflict == ConflictPolicy.Rename && merging)
        {
            target = UniqueName(target);
            merging = false;
        }

        // 같은 볼륨의 이동이고 합칠 것이 없으면 통째로 이름만 바꾼다 — 트리를 걷지 않는다.
        // (Directory.Move 는 볼륨을 건너뛰지 못한다 — 확인함. 그때는 복사 후 삭제로 간다.)
        if (request.Op == FileOperationKind.Move && !merging && SameVolume(source.Path, target))
        {
            walk.Touch(source.Path);
            Directory.Move(source.Path, target);
            return ok(false);
        }

        var stats = CopyTree(source.Path, target, request.OnConflict, walk, token);

        if (token.IsCancellationRequested)
        {
            // 취소가 절반 처리 상태를 숨기면 안 된다 — 목적지에 남은 것을 사유에 적는다.
            // 이동이어도 원본은 그대로다(아래 삭제 단계까지 오지 못했다).
            return fail(
                $"취소 — 파일 {stats.Copied}개까지 복사했다. 목적지에 일부가 남았다" +
                (request.Op == FileOperationKind.Move ? " (원본은 그대로)" : string.Empty),
                stats.Overwritten > 0);
        }

        if (stats.Failures.Count > 0)
        {
            return fail(
                $"{stats.Failures.Count}개 항목 실패 — {stats.Failures[0]}",
                stats.Overwritten > 0);
        }

        if (request.Op == FileOperationKind.Move)
        {
            // 다 옮겨 담은 뒤에만 원본을 지운다. 중간에 실패했으면 원본은 남는다.
            Directory.Delete(source.Path, recursive: true);
        }

        return ok(stats.Overwritten > 0);
    }

    /// <summary>재귀 복사. 개별 파일 충돌은 §4.3 규칙 그대로.</summary>
    private static TreeStats CopyTree(
        string sourceDir,
        string targetDir,
        ConflictPolicy onConflict,
        WalkCounter walk,
        CancellationToken token)
    {
        var stats = new TreeStats();
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (token.IsCancellationRequested)
            {
                return stats;
            }

            var target = Path.Combine(targetDir, Path.GetFileName(file));
            var overwriting = File.Exists(target);

            if (overwriting && onConflict == ConflictPolicy.Skip)
            {
                continue;
            }

            if (overwriting && onConflict == ConflictPolicy.Rename)
            {
                target = UniqueName(target);
                overwriting = false;
            }

            try
            {
                walk.Touch(file);
                File.Copy(file, target, overwrite: true);
                stats.Copied++;
                if (overwriting)
                {
                    stats.Overwritten++;
                }
            }
            catch (Exception ex)
            {
                stats.Failures.Add($"{Path.GetFileName(file)}: {Describe(ex)}");
            }
        }

        foreach (var child in Directory.EnumerateDirectories(sourceDir))
        {
            if (token.IsCancellationRequested)
            {
                return stats;
            }

            var nested = CopyTree(
                child, Path.Combine(targetDir, Path.GetFileName(child)), onConflict, walk, token);

            stats.Copied += nested.Copied;
            stats.Overwritten += nested.Overwritten;
            stats.Failures.AddRange(nested.Failures);
        }

        return stats;
    }

    private sealed class TreeStats
    {
        public int Copied { get; set; }

        public int Overwritten { get; set; }

        public List<string> Failures { get; } = [];
    }

    /// <summary>진행 보고를 한 군데로 모은다 — 단위 수와 실제 파일 수를 같이 센다.</summary>
    private sealed class WalkCounter(IProgress<FileOperationProgress>? progress, int totalUnits)
    {
        private int _completedUnits;
        private int _entries;

        public void BeginUnit(string path) => Publish(path);

        public void Touch(string path)
        {
            _entries++;
            Publish(path);
        }

        public void EndUnit()
        {
            _completedUnits++;
            Publish(null);
        }

        private void Publish(string? path) =>
            progress?.Report(new FileOperationProgress(_completedUnits, totalUnits, path, _entries));
    }

    // ── 경로 헬퍼 ────────────────────────────────────────────────────────────

    private static string TargetOf(OperationItem source, string destination) =>
        Path.Combine(destination, Path.GetFileName(source.Path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool CanReach(string destination) =>
        !string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination);

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <b>제자리 복제</b>인가 — 원본이 목적지 폴더에 이미 있고, 복사이고, 「둘 다 두기」인가
    /// (Phase 8-D ②).
    /// </summary>
    /// <remarks>
    /// 이 셋이 다 맞을 때만 "원본과 목적지가 같다"는 건너뛰기를 지나간다. 지나가고 나면
    /// <see cref="ConflictPolicy.Rename"/> 가 평소처럼 <see cref="UniqueName"/> 을 불러
    /// <c>a.txt</c> 를 <c>a (2).txt</c> 로 만든다 — <b>새 이름 규칙을 만들지 않았다</b>.
    /// <para>
    /// <b>이동은 여기 들지 않는다.</b> 같은 폴더로 옮기면서 이름만 바꾸는 것은 이동이 아니라
    /// 이름 바꾸기다(§4.6 이 따로 갖고 있다). 그래서 이동은 지금처럼 건너뛴다.
    /// </para>
    /// <para>
    /// <b>덮어쓰기·건너뛰기도 들지 않는다.</b> 덮어쓰기는 자기를 자기로 덮는 것이라 할 일이
    /// 없고, 건너뛰기는 이름이 겹칠 때 안 하겠다는 뜻이라 결론이 같다. 둘 다 지금 그대로다.
    /// </para>
    /// </remarks>
    private static bool IsInPlaceDuplicate(FileOperationRequest request, string sourcePath, string target) =>
        request.Op == FileOperationKind.Copy
        && request.OnConflict == ConflictPolicy.Rename
        && SamePath(sourcePath, target);


    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string parent)
    {
        var full = Normalize(path);
        var root = Normalize(parent);

        return full.Length > root.Length
               && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && (full[root.Length] == Path.DirectorySeparatorChar
                   || full[root.Length] == Path.AltDirectorySeparatorChar);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// 「둘 다 두기」가 지을 이름을 <b>미리</b> 알려준다 (Phase 8-E ②).
    /// </summary>
    /// <remarks>
    /// 확인창이 "a.txt → a (2).txt" 를 적으려면 그 이름을 알아야 하는데, <b>형식을 화면 쪽에
    /// 다시 쓰면 두 곳이 어긋난다</b>. 그래서 실제로 쓰는 <see cref="UniqueName"/> 를 그대로
    /// 연다 — 여는 것은 이 하나뿐이고 셈은 한 글자도 바뀌지 않았다.
    /// <para>
    /// <b>지금 파일 시스템을 본다.</b> 부른 시점에 비어 있는 첫 이름을 돌려주므로, 창을 띄운
    /// 뒤 밖에서 같은 이름이 생기면 실제 결과가 달라질 수 있다. 한 뭉치 안의 항목들끼리는
    /// 어긋나지 않는다 — 같은 폴더의 항목은 이름이 서로 달라 지어지는 이름도 겹치지 않는다.
    /// </para>
    /// </remarks>
    public static string PreviewUniqueName(string target) => UniqueName(target);

    /// <summary>"a.txt" 가 있으면 "a (2).txt" — 탐색기와 같은 감각.</summary>
    private static string UniqueName(string target)
    {
        var directory = Path.GetDirectoryName(target) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        for (var n = 2; n < int.MaxValue; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!Exists(candidate))
            {
                return candidate;
            }
        }

        return target;
    }

    // ── 사유는 사람이 읽을 수 있게 (§5) ──────────────────────────────────────

    private const int ErrorSharingViolation = 0x20;
    private const int ErrorLockViolation = 0x21;
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorDiskFull = 0x70;

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "권한이 없다",
        FileNotFoundException => "원본이 없다",
        DirectoryNotFoundException => "경로가 없다",
        PathTooLongException => "경로가 너무 길다",
        IOException io when Win32Code(io) is ErrorDiskFull or ErrorHandleDiskFull => "디스크 공간이 부족하다",
        IOException io when Win32Code(io) is ErrorSharingViolation or ErrorLockViolation
            => "다른 프로그램이 쓰고 있다",
        _ => ex.Message,
    };

    private static int Win32Code(IOException ex) => ex.HResult & 0xFFFF;
}
