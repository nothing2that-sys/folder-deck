using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>
/// 왼쪽 폴더 리스트의 한 행. 이 리스트가 순환 칸의 유일한 컨트롤러다 (§2.3 · §3.2).
/// </summary>
public sealed partial class FolderRowViewModel(FolderEntry entry) : ObservableObject, IFileDropTarget
{
    public FolderEntry Entry { get; } = entry;

    /// <summary>그리드에서 끌어온 파일이 이 행 위에 있는가 (§4.2-2 드롭 과녁).</summary>
    [ObservableProperty]
    private bool isDropTarget;

    public string DisplayName => string.IsNullOrWhiteSpace(Entry.DisplayName)
        ? System.IO.Path.GetFileName(Entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Entry.Path
        : Entry.DisplayName!;

    public string PathText => Entry.Path;

    public string? Description => Entry.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);

    /// <summary>
    /// 상시(실선 pin) 대 순환 전용(점선 글리프). §3.2
    /// <para>
    /// Phase 2 부터 "상시"는 <b>상시 타일이 있다</b>는 뜻이다 — <c>folders[].pinned</c> 가
    /// 아니라. 옛 플래그를 그대로 보면 빈 칸에 폴더를 넣어 상시로 만들어도 글리프가
    /// 순환인 채로 남는다(실제로 그랬다). 저장 필드는 이 상태의 그림자로 따라간다.
    /// </para>
    /// </summary>
    public bool Pinned => HasPinnedTile;

    /// <summary>앵커를 벗어나 있으면 그 상태 — 예: "지금 src › Recipe 안" (§3.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocationHint))]
    private string? locationHint;

    public bool HasLocationHint => !string.IsNullOrEmpty(LocationHint);

    /// <summary>
    /// 접근 불가 상태. <b>이 상태여도 목록에서 지우지 않는다</b> —
    /// 네트워크 경로가 복구되면 다시 조회된다(§5).
    /// </summary>
    [ObservableProperty]
    private bool isInaccessible;

    /// <summary>지금 순환 칸에 떠 있는 폴더인지(행 강조용).</summary>
    [ObservableProperty]
    private bool isShowingInRotating;

    // ── 순환 번호 (Phase 2) ──────────────────────────────────────────────────

    /// <summary>이 폴더를 클릭하면 뜰 순환 칸 번호. null(=순환1)도 1 로 보여준다.</summary>
    public int RotationSlot => Entry.RotationSlot ?? 1;

    public string RotationSlotText => $"순환{RotationSlot}";

    /// <summary>
    /// 태그를 행에 붙일지. 순환 칸이 하나뿐이면 모든 행에 "순환1"이 붙어 봐야 소음이고,
    /// 상시 칸에 못박힌 폴더는 애초에 순환 칸으로 가지 않는다.
    /// </summary>
    [ObservableProperty]
    private bool showRotationTag;

    /// <summary>
    /// 이 폴더가 이미 상시 칸에 못박혀 있는가. 그러면 빈 칸에 또 넣을 수 없고(같은 폴더를
    /// 두 상시 칸에 두는 건 칸 낭비), 클릭해도 순환 칸이 아니라 자기 상시 칸이 반응한다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlaceInGrid))]
    [NotifyPropertyChangedFor(nameof(Pinned))]
    private bool hasPinnedTile;

    /// <summary>배치 편집에서 빈 칸에 넣을 수 있는 폴더인가.</summary>
    public bool CanPlaceInGrid => !HasPinnedTile;

    /// <summary>우클릭 메뉴 "순환 칸 지정 ▸ …" 항목들. 배치가 바뀌면 다시 만들어진다.</summary>
    [ObservableProperty]
    private IReadOnlyList<RotationChoiceViewModel> rotationChoices = [];

    public void NotifyRotationSlot()
    {
        OnPropertyChanged(nameof(RotationSlot));
        OnPropertyChanged(nameof(RotationSlotText));
    }

    /// <summary>
    /// 표시 이름·설명을 고쳤다 (§4.11-3). 셋 다 <see cref="Entry"/> 에서 계산되므로
    /// 알림이 저절로 걸리지 않는다 — <see cref="NotifyRotationSlot"/> 과 같은 결로 손으로 알린다.
    /// </summary>
    public void NotifyEdited()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasDescription));
    }
}
