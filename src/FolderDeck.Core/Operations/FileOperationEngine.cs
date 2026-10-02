using FolderDeck.Core.Models;
using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Operations;


















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


        return Task.Run(() => Execute(request, progress, cancellationToken), CancellationToken.None);
    }



    private static FileOperationPreview Preview(FileOperationRequest request)
    {
        if (request.Op == FileOperationKind.Trash)
        {

            return new FileOperationPreview(request.Sources.Count, 0, [], []);
        }

        var perDestination = new List<DestinationPreview>();
        var skipped = new List<string>();

        foreach (var destination in request.Destinations)
        {
            if (!CanReach(destination))
            {

                skipped.Add(destination);
                continue;
            }


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




        if (!IsInPlaceDuplicate(request, source.Path, target) && SamePath(source.Path, target))
        {
            return skip("원본과 목적지가 같다");
        }


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



        if (request.Op == FileOperationKind.Move && !merging
            && FolderPathRules.SameVolume(source.Path, target))
        {
            walk.Touch(source.Path);
            Directory.Move(source.Path, target);
            return ok(false);
        }

        var stats = CopyTree(source.Path, target, request.OnConflict, walk, token);

        if (token.IsCancellationRequested)
        {


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

            if (!TryDeleteSourceTree(source.Path, out var deleteError))
            {




                return fail(
                    $"복사는 끝났지만 원본을 지우지 못했다 — {deleteError} " +
                    "(목적지에 이미 있다, 원본도 그대로 남아 있다)",
                    stats.Overwritten > 0);
            }
        }

        return ok(stats.Overwritten > 0);
    }













    private static bool TryDeleteSourceTree(string path, out string? error)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return false;
        }
    }


    private static TreeStats CopyTree(
        string sourceDir,
        string targetDir,
        ConflictPolicy onConflict,
        WalkCounter walk,
        CancellationToken token)
    {
        var stats = new TreeStats();
        Directory.CreateDirectory(targetDir);

        try
        {
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
        }
        catch (Exception ex)
        {



            stats.Failures.Add($"(파일 목록을 끝까지 읽지 못했다: {Describe(ex)})");
        }

        try
        {
            foreach (var child in Directory.EnumerateDirectories(sourceDir))
            {
                if (token.IsCancellationRequested)
                {
                    return stats;
                }

                if (new DirectoryInfo(child).LinkTarget is not null)
                {







                    stats.Failures.Add($"{Path.GetFileName(child)}: 정션/링크라 건너뛰었다");
                    continue;
                }

                var nested = CopyTree(
                    child, Path.Combine(targetDir, Path.GetFileName(child)), onConflict, walk, token);

                stats.Copied += nested.Copied;
                stats.Overwritten += nested.Overwritten;
                stats.Failures.AddRange(nested.Failures);
            }
        }
        catch (Exception ex)
        {
            stats.Failures.Add($"(하위 폴더 목록을 끝까지 읽지 못했다: {Describe(ex)})");
        }

        return stats;
    }

    private sealed class TreeStats
    {
        public int Copied { get; set; }

        public int Overwritten { get; set; }

        public List<string> Failures { get; } = [];
    }


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



    private static string TargetOf(OperationItem source, string destination) =>
        Path.Combine(destination, Path.GetFileName(source.Path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool CanReach(string destination) =>
        !string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination);

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);


















    private static bool IsInPlaceDuplicate(FileOperationRequest request, string sourcePath, string target) =>
        request.Op == FileOperationKind.Copy
        && request.OnConflict == ConflictPolicy.Rename
        && SamePath(sourcePath, target);

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














    public static string PreviewUniqueName(string target) => UniqueName(target);


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
