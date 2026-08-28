using FolderDeck.App.Services;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 설정 창 「검색」 탭의 DataContext (D65 결정 8). 상태 한 줄만 보여 준다 — 검색 타일은
/// 이 회차에서 만들지 않는다.
/// </summary>
public sealed class EverythingSettingsViewModel(EverythingStatus status)
{
    public string StatusText { get; } = status.Describe();
}
