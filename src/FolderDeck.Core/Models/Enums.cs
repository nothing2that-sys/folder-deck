namespace FolderDeck.Core.Models;

/// <summary>파일 목록 표시 방식 (§7.1 <c>viewMode</c>).</summary>
public enum FolderViewMode
{
    List,
    Details,
    LargeIcons,
    ExtraLargeIcons,
}

/// <summary>파일 목록 정렬 기준 (§7.1 <c>sortBy</c>).</summary>
public enum SortBy
{
    Name,
    Modified,
    Size,
    Type,
}

/// <summary>파일 조작 종류 (§4.7 <c>op</c>). 실제 동작은 Phase 3.</summary>
public enum FileOperationKind
{
    Copy,
    Move,
    Trash,
}

/// <summary>동일 이름 충돌 처리 (§4.3 / §4.7 <c>onConflict</c>). 실제 동작은 Phase 3.</summary>
public enum ConflictPolicy
{
    Overwrite,
    Skip,
    Rename,
}
