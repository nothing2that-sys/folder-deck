using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 설정 창 「검색」 탭의 DataContext (D65 결정 8 · D69-d 가 결과 상한을 더함). 저장을 눌러야
/// <see cref="EverythingMaxResults"/> 가 반영된다 — <see cref="IconSettingsViewModel"/> 과
/// 같은 규칙이다.
/// </summary>
public sealed class EverythingSettingsViewModel(EverythingStatus status, int everythingMaxResults)
{
    public string StatusText { get; } = status.Describe();

    /// <summary>램프 고르개(<c>ComboBox</c>)가 보여 줄 다섯 값 고정(D69-d 결정 6).</summary>
    public IReadOnlyList<int> MaxResultsChoices => AppSettings.EverythingMaxResultsChoices;

    public int EverythingMaxResults { get; set; } = everythingMaxResults;
}
