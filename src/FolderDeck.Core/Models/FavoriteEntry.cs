namespace FolderDeck.Core.Models;

/// <summary>
/// 즐겨찾기 하나 (2026-10-14 S3). 「바로 이동」 목록일 뿐이다 — 작업 관리별 상태(보기 방식·타일
/// 배치)를 갖는 등록 폴더(<see cref="FolderEntry"/>)와 섞지 않는다.
/// </summary>
public sealed class FavoriteEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Path { get; set; } = string.Empty;

    /// <summary>비어 있으면 화면은 경로의 마지막 조각을 쓴다.</summary>
    public string? DisplayName { get; set; }
}

/// <summary><c>favorites.json</c> 파일 한 벌.</summary>
public sealed class FavoritesDocument
{
    /// <summary>1 — 처음 판(2026-10-14 S3).</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    public List<FavoriteEntry> Items { get; set; } = [];
}
