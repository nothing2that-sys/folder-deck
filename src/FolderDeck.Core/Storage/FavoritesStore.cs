using System.Text.Json;
using FolderDeck.Core.Models;
using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Storage;

/// <summary><see cref="FavoritesStore.Mutate"/> 의 결과 — 적용 뒤 목록과 실제로 썼는지.</summary>
public sealed record FavoritesMutation(IReadOnlyList<FavoriteEntry> Items, bool Changed);

/// <summary>
/// 모든 작업 관리가 함께 쓰는 즐겨찾기 (2026-10-14 S3). <c>%APPDATA%\FolderDeck\favorites.json</c>.
/// </summary>
/// <remarks>
/// 작업 관리마다 프로세스가 따로라 「메모리 상태를 통째로 쓰기」는 다른 창이 그 사이에 한 변경을
/// 지운다. 그래서 변경은 언제나 <b>이름 있는 뮤텍스 안에서 디스크를 다시 읽고 → 적용하고 → 원자적으로
/// 쓴다</b>(<see cref="Mutate"/>). 메모리에 든 목록은 화면용 사본일 뿐 쓰기의 출발점이 아니다.
/// <para>
/// <b>손상된 파일 위에는 쓰지 않는다</b> — 그러면 사용자가 손으로 고칠 길이 지워진다(§5 「실패를
/// 삼키지 않는다」). 파일이 없으면 빈 목록이다(첫 실행).
/// </para>
/// </remarks>
public sealed class FavoritesStore
{
    /// <summary>프로세스를 건너 변경을 한 줄로 세우는 뮤텍스. 세션 안(<c>Local\</c>)이면 충분하다.</summary>
    public const string MutexName = @"Local\FolderDeck.Favorites";

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    public FavoritesStore(FolderDeckPaths paths)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public FolderDeckPaths Paths { get; }

    public string FilePath => Paths.FavoritesFile;

    /// <summary>읽기만 한다. 파일이 없으면 빈 목록.</summary>
    public StorageResult<List<FavoriteEntry>> Load()
    {
        string text;
        try
        {
            if (!File.Exists(FilePath))
            {
                return StorageResult<List<FavoriteEntry>>.Ok([]);
            }

            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex)
        {
            return StorageResult<List<FavoriteEntry>>.Fail(Describe(ex));
        }

        FavoritesDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<FavoritesDocument>(text, FolderDeckJson.Options);
        }
        catch (JsonException ex)
        {
            return StorageResult<List<FavoriteEntry>>.Fail(
                new StorageFailure(StorageFailureKind.CorruptData, FilePath, ex.Message, ex));
        }

        if (document is null)
        {
            return StorageResult<List<FavoriteEntry>>.Fail(
                new StorageFailure(StorageFailureKind.CorruptData, FilePath, "내용이 비었거나 null 이다."));
        }

        if (document.SchemaVersion > FavoritesDocument.CurrentSchemaVersion)
        {
            return StorageResult<List<FavoriteEntry>>.Fail(new StorageFailure(
                StorageFailureKind.UnsupportedSchemaVersion, FilePath,
                $"schemaVersion {document.SchemaVersion} 은(는) 이 버전이 모르는 형식이다 " +
                $"(지원: {FavoritesDocument.CurrentSchemaVersion})."));
        }

        if (document.SchemaVersion < 1)
        {
            return StorageResult<List<FavoriteEntry>>.Fail(new StorageFailure(
                StorageFailureKind.InvalidContent, FilePath, "schemaVersion 이 없거나 잘못됐다."));
        }

        // 손으로 고친 파일에 null 항목·빈 경로가 섞여도 목록 전체를 버리지 않는다.
        return StorageResult<List<FavoriteEntry>>.Ok(
            [.. (document.Items ?? []).Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path))]);
    }

    /// <summary>
    /// 디스크에서 다시 읽고 <paramref name="change"/> 를 적용해 쓴다. <paramref name="change"/> 가
    /// false 면 쓰지 않는다. 읽기가 실패하면(손상·모르는 판) <b>쓰지 않고</b> 그 실패를 돌려준다.
    /// </summary>
    public StorageResult<FavoritesMutation> Mutate(Func<List<FavoriteEntry>, bool> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                // 앞 주인이 정리 없이 죽었다. 파일 쓰기는 원자적이라 반쯤 쓴 파일은 없다 — 인수한다.
                owned = true;
            }

            if (!owned)
            {
                return StorageResult<FavoritesMutation>.Fail(new StorageFailure(
                    StorageFailureKind.IoError, FilePath, "다른 창이 즐겨찾기를 쓰는 중이라 기다리다 그만뒀다."));
            }

            var loaded = Load();
            if (loaded.Value is not { } items)
            {
                return StorageResult<FavoritesMutation>.Fail(loaded.Failure!);
            }

            if (!change(items))
            {
                return StorageResult<FavoritesMutation>.Ok(new FavoritesMutation(items, Changed: false));
            }

            var document = new FavoritesDocument
            {
                SchemaVersion = FavoritesDocument.CurrentSchemaVersion,
                Items = items,
            };

            try
            {
                Directory.CreateDirectory(Paths.Root);
                AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(document, FolderDeckJson.Options));
            }
            catch (Exception ex)
            {
                return StorageResult<FavoritesMutation>.Fail(Describe(ex));
            }

            return StorageResult<FavoritesMutation>.Ok(new FavoritesMutation(items, Changed: true));
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    // ── 자주 쓰는 변경 ───────────────────────────────────────────────────────

    /// <summary>같은 경로(<see cref="SamePath"/>)가 이미 있으면 쓰지 않는다(<c>Changed = false</c>).</summary>
    public StorageResult<FavoritesMutation> Add(string path, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = FolderPathRules.Normalize(path);

        return Mutate(items =>
        {
            if (items.Any(i => SamePath(i.Path, normalized)))
            {
                return false;
            }

            items.Add(new FavoriteEntry { Path = normalized, DisplayName = displayName });
            return true;
        });
    }

    public StorageResult<FavoritesMutation> Remove(Guid id) =>
        Mutate(items => items.RemoveAll(i => i.Id == id) > 0);

    /// <summary>한 칸 옮긴다(<paramref name="delta"/> = -1 위, +1 아래). 끝에서 더 못 가면 쓰지 않는다.</summary>
    public StorageResult<FavoritesMutation> Move(Guid id, int delta) =>
        Mutate(items =>
        {
            var index = items.FindIndex(i => i.Id == id);
            var target = index + delta;
            if (index < 0 || delta == 0 || target < 0 || target >= items.Count)
            {
                return false;
            }

            var item = items[index];
            items.RemoveAt(index);
            items.Insert(target, item);
            return true;
        });

    /// <summary>윈도우 파일 시스템처럼 대소문자를 가리지 않고, 끝 구분자를 떼고 비교한다.</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(FolderPathRules.Normalize(a), FolderPathRules.Normalize(b), StringComparison.OrdinalIgnoreCase);

    private StorageFailure Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new StorageFailure(StorageFailureKind.AccessDenied, FilePath, ex.Message, ex),
        FileNotFoundException => new StorageFailure(StorageFailureKind.NotFound, FilePath, ex.Message, ex),
        DirectoryNotFoundException => new StorageFailure(StorageFailureKind.NotFound, FilePath, ex.Message, ex),
        _ => new StorageFailure(StorageFailureKind.IoError, FilePath, ex.Message, ex),
    };
}
