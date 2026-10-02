using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;
using IoPath = System.IO.Path;

namespace FolderDeck.App.ViewModels;

public sealed partial class WorkspaceCardViewModel : ObservableObject
{

    private const int MaxPinnedChips = 4;

    private readonly IWorkspaceStore? _store;

    public WorkspaceCardViewModel(Workspace workspace, IWorkspaceStore store)
    {
        Workspace = workspace;
        _store = store;
        title = workspace.Title;
        description = workspace.Description;
    }

    public WorkspaceCardViewModel(StorageFailure failure)
    {
        Failure = failure;
        title = IoPath.GetFileName(failure.Path);
    }

    public Workspace? Workspace { get; }

    public StorageFailure? Failure { get; }

    public bool IsCorrupt => Workspace is null;

    public bool IsUsable => Workspace is not null;

    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenButtonText))]
    private bool isOpen;

    public string OpenButtonText => IsOpen ? "열려 있음" : "열기";

    public string PrimaryPath
    {
        get
        {
            if (Workspace is null)
            {
                return Failure?.Path ?? string.Empty;
            }

            return Workspace.Folders.Count > 0
                ? Workspace.Folders[0].Path
                : _store?.Paths.WorkspaceFile(Workspace.Id) ?? string.Empty;
        }
    }

    public IReadOnlyList<string> PinnedChips => Workspace is null
        ? []
        : [.. Workspace.Folders.Where(f => f.Pinned).Take(MaxPinnedChips).Select(NameOf)];

    public string? MorePinnedText
    {
        get
        {
            if (Workspace is null)
            {
                return null;
            }

            var extra = Workspace.Folders.Count(f => f.Pinned) - MaxPinnedChips;
            return extra > 0 ? $"+{extra}" : null;
        }
    }

    public bool HasMorePinned => MorePinnedText is not null;

    public int RotatingCount => Workspace?.Folders.Count(f => !f.Pinned) ?? 0;

    public string RotatingChipText => $"+ 순환 {RotatingCount}";

    public bool HasRotatingChip => RotatingCount > 0;

    public bool HasNoFolders => Workspace is not null && Workspace.Folders.Count == 0;

    public string PinnedNamesText
    {
        get
        {
            if (Workspace is null)
            {
                return string.Empty;
            }

            var pinned = Workspace.Folders.Where(f => f.Pinned).ToList();
            if (pinned.Count == 0)
            {
                return string.Empty;
            }

            var shown = string.Join(" · ", pinned.Take(MaxPinnedChips).Select(NameOf));
            var extra = pinned.Count - MaxPinnedChips;
            return extra > 0 ? $"{shown} +{extra}" : shown;
        }
    }

    public bool HasPinnedNames => PinnedNamesText.Length > 0;

    public string RotatingNamesText
    {
        get
        {
            if (Workspace is null)
            {
                return string.Empty;
            }

            var rotating = Workspace.Folders.Where(f => !f.Pinned).ToList();
            if (rotating.Count == 0)
            {
                return string.Empty;
            }

            var shown = string.Join(" · ", rotating.Take(MaxPinnedChips).Select(NameOf));
            var extra = rotating.Count - MaxPinnedChips;
            return extra > 0 ? $"{shown} +{extra}" : shown;
        }
    }

    public bool HasRotatingNames => RotatingNamesText.Length > 0;

    public string PinnedNamesRun => HasPinnedNames ? " · " + PinnedNamesText : string.Empty;

    public string RotatingNamesRun => HasRotatingNames ? " · " + RotatingNamesText : string.Empty;

    public string FolderNamesText => Workspace is null
        ? string.Empty
        : FolderCountText + PinnedNamesRun + RotatingNamesRun;

    public string LastUsedText => Workspace is null
        ? string.Empty
        : Workspace.LastUsed == default
            ? "사용 기록 없음"
            : $"마지막 사용 {Workspace.LastUsed.LocalDateTime:yyyy-MM-dd HH:mm}";

    public string FolderCountText => Workspace is null ? string.Empty : $"폴더 {Workspace.Folders.Count}";

    public string MetaText =>
        Workspace is null ? string.Empty : $"{FolderCountText} · {LastUsedText}";

    public string CorruptText =>
        "이 작업 관리 파일을 읽지 못했습니다. 다른 항목은 정상입니다.";

    public string? CorruptDetail => Failure?.Message;

    public string? PathText => Failure?.Path;

    public bool Matches(string query) =>
        (Title ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
        || PrimaryPath.Contains(query, StringComparison.OrdinalIgnoreCase);

    partial void OnTitleChanged(string? value)
    {
        if (Workspace is null || _store is null)
        {
            return;
        }

        Workspace.Title = value ?? string.Empty;
        _store.SaveWorkspaceDebounced(Workspace);
    }

    partial void OnDescriptionChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDescription));

        if (Workspace is null || _store is null)
        {
            return;
        }

        Workspace.Description = string.IsNullOrWhiteSpace(value) ? null : value;
        _store.SaveWorkspaceDebounced(Workspace);
    }

    private static string NameOf(FolderEntry folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.DisplayName))
        {
            return folder.DisplayName!;
        }

        var trimmed = folder.Path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        var name = IoPath.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
