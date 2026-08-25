using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;
using IoPath = System.IO.Path;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 런처 목록의 카드 한 장 (§3.1). 정상 워크스페이스이거나, 읽지 못한 파일이다.
/// </summary>
/// <remarks>
/// 손상된 파일도 목록에서 <b>지우지 않고</b> "손상"으로 남긴다 — 조용히 건너뛰면
/// 사용자가 파일이 사라진 줄 안다(§5).
/// </remarks>
public sealed partial class WorkspaceCardViewModel : ObservableObject
{
    /// <summary>칩이 카드를 잡아먹지 않게 상시 폴더는 이만큼만 보여준다.</summary>
    private const int MaxPinnedChips = 4;

    private readonly IWorkspaceStore? _store;

    /// <summary>정상 항목.</summary>
    public WorkspaceCardViewModel(Workspace workspace, IWorkspaceStore store)
    {
        Workspace = workspace;
        _store = store;
        title = workspace.Title;
        description = workspace.Description;
    }

    /// <summary>읽지 못한 파일.</summary>
    public WorkspaceCardViewModel(StorageFailure failure)
    {
        Failure = failure;
        title = IoPath.GetFileName(failure.Path);
    }

    public Workspace? Workspace { get; }

    public StorageFailure? Failure { get; }

    /// <summary>읽지 못한 파일이면 true — 열 수도 수정할 수도 없다(id 를 못 읽는다).</summary>
    public bool IsCorrupt => Workspace is null;

    public bool IsUsable => Workspace is not null;

    /// <summary>인라인 편집 대상. 바뀌면 즉시 저장된다(디바운스 + 원자적 쓰기).</summary>
    [ObservableProperty]
    private string? title;

    [ObservableProperty]
    private string? description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>"수정"으로 켜는 편집 모드. 평소엔 읽기 전용으로 보여 카드가 깔끔하다.</summary>
    [ObservableProperty]
    private bool isEditing;

    /// <summary>
    /// 한 번 클릭으로 고른 카드 (Phase 8-A ④). <b>저장하지 않는 런타임 상태다</b> —
    /// 고르는 것 자체는 아무 일도 하지 않고, 여는 것은 더블클릭이나 "열기" 버튼이다.
    /// </summary>
    [ObservableProperty]
    private bool isSelected;

    /// <summary>
    /// 이 워크스페이스 창이 지금 떠 있는가 (§4.2). <b>힌트다</b> — 런처 활성화 때 다시 재고,
    /// 실제 판정은 버튼을 누를 때 한다. 저장하지 않는 런타임 상태다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenButtonText))]
    private bool isOpen;

    /// <summary>
    /// 버튼 글씨가 곧 동작이다 (§3.1) — 이미 열린 것을 눌러도 아무 일도 일어나지 않으므로
    /// (§6.2), 누르기 전에 화면이 그것을 말해 "눌렀는데 아무 반응이 없다"는 순간을 없앤다.
    /// 배지의 색만으로 구분하지 않는다는 결(§3.2)도 여기서 지켜진다.
    /// </summary>
    public string OpenButtonText => IsOpen ? "열려 있음" : "열기";

    /// <summary>
    /// 대표 경로 — 개발자는 경로로 워크스페이스를 알아본다. 첫 폴더가 있으면 그것,
    /// 폴더가 하나도 없으면 워크스페이스 파일 위치.
    /// </summary>
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

    /// <summary>상시(pinned) 폴더 이름 칩. 메인 창의 상시/순환 감각과 같은 뜻이다(§2.3).</summary>
    public IReadOnlyList<string> PinnedChips => Workspace is null
        ? []
        : [.. Workspace.Folders.Where(f => f.Pinned).Take(MaxPinnedChips).Select(NameOf)];

    /// <summary>상시가 <see cref="MaxPinnedChips"/> 를 넘으면 나머지 개수만.</summary>
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

    /// <summary>순환 전용 폴더는 개수 칩 하나로 접는다.</summary>
    public string RotatingChipText => $"+ 순환 {RotatingCount}";

    public bool HasRotatingChip => RotatingCount > 0;

    /// <summary>폴더가 하나도 없는 워크스페이스 — 칩이 아무것도 없으면 그렇게 말한다.</summary>
    public bool HasNoFolders => Workspace is not null && Workspace.Folders.Count == 0;

    /// <summary>
    /// 상시 폴더 이름을 한 줄로 (Phase 8-A ⑥ 3줄째). <see cref="PinnedChips"/> 와 같은 재료를
    /// 같은 상한으로 자르되, 칩 여러 개가 줄바꿈해 카드를 늘리지 않도록 문자열 하나로 준다.
    /// </summary>
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

    /// <summary>마지막 사용 시각 한 조각 (⑥ 2줄째).</summary>
    public string LastUsedText => Workspace is null
        ? string.Empty
        : Workspace.LastUsed == default
            ? "사용 기록 없음"
            : $"마지막 사용 {Workspace.LastUsed.LocalDateTime:yyyy-MM-dd HH:mm}";

    /// <summary>폴더 개수 한 조각 (⑥ 2줄째).</summary>
    public string FolderCountText => Workspace is null ? string.Empty : $"폴더 {Workspace.Folders.Count}";

    /// <summary>두 조각을 합친 한 줄. 화면은 ⑥ 이후 조각을 따로 쓰지만 뜻은 같다.</summary>
    public string MetaText =>
        Workspace is null ? string.Empty : $"{FolderCountText} · {LastUsedText}";

    public string CorruptText =>
        "이 작업 관리 파일을 읽지 못했습니다. 다른 항목은 정상입니다.";

    public string? CorruptDetail => Failure?.Message;

    public string? PathText => Failure?.Path;

    /// <summary>필터가 보는 문자열 — 제목과 경로로 좁힌다.</summary>
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
