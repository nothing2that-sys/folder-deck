using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;






public sealed class EverythingSettingsViewModel(EverythingStatus status, int everythingMaxResults)
{
    public string StatusText { get; } = status.Describe();


    public IReadOnlyList<int> MaxResultsChoices => AppSettings.EverythingMaxResultsChoices;

    public int EverythingMaxResults { get; set; } = everythingMaxResults;
}
