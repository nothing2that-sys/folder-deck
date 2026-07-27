namespace FolderDeck.App.ViewModels;

/// <summary>
/// 타일 헤더 breadcrumb 의 한 조각 (§3.2 · §4.6).
/// <see cref="IsAnchor"/> 인 첫 조각을 누르면 앵커로 돌아온다(3경로 중 하나).
/// </summary>
public sealed record BreadcrumbSegment(string Name, string Path, bool IsAnchor);
