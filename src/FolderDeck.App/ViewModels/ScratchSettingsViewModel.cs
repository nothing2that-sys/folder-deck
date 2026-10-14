using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>설정 창의 고르개 한 줄 — 프리셋과 보이는 이름.</summary>
public sealed record LayoutPresetChoice(LayoutPreset Preset, string Label);

/// <summary>설정 창 「1회용 창」 탭 — 처음 배치(S2) · 단축키(S4) · 받는 쪽(2차) (2026-10-14).</summary>
public sealed partial class ScratchSettingsViewModel : ObservableObject
{
    private readonly bool _shortcutInstalled;
    private readonly bool _agentRunning;

    public ScratchSettingsViewModel(
        LayoutPreset current,
        string hotkey = ScratchHotkey.Default,
        bool shortcutInstalled = false,
        bool useResidentAgent = true,
        bool agentRunning = false)
    {
        Layouts = [.. LayoutPresets.All.Select(p => new LayoutPresetChoice(p, LayoutPresets.Label(p)))];
        SelectedLayout = Layouts.FirstOrDefault(c => c.Preset == current) ?? Layouts[0];

        _shortcutInstalled = shortcutInstalled;
        _agentRunning = agentRunning;
        hotkeyText = ScratchHotkey.TryNormalize(hotkey, out var normalized) ? normalized : ScratchHotkey.Default;
        this.useResidentAgent = useResidentAgent;
    }

    public IReadOnlyList<LayoutPresetChoice> Layouts { get; }

    public LayoutPresetChoice SelectedLayout { get; set; }

    /// <summary>지금 고른 단축키(정규형). 빈 문자열은 「없음」.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private string hotkeyText;

    /// <summary>칸에 보이는 글자 — 「없음」을 빈 칸으로 두지 않는다.</summary>
    public string HotkeyDisplay => HotkeyText.Length == 0 ? "없음" : HotkeyText;

    /// <summary>방금 누른 조합을 왜 안 받았는지. 받았으면 null.</summary>
    [ObservableProperty]
    private string? hotkeyMessage;

    /// <summary>
    /// 단축키를 상주 프로그램이 받을지(켬 · 빠름) 시작 메뉴 바로가기가 받을지(끔 · 상주 없음 · 느림). 체크박스가
    /// 바인딩으로만 오간다 — 이벤트 핸들러가 없어 생성 도중 함정이 없다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutingStatusText))]
    private bool useResidentAgent;

    /// <summary>지금 고른 쪽에서 무슨 일이 일어나는지 한 줄로.</summary>
    public string RoutingStatusText => UseResidentAgent
        ? _agentRunning
            ? "상주 프로그램이 실행 중이다 — 저장하면 새 단축키를 바로 받는다. 다른 프로그램이 같은 키를 쓰면 트레이 알림으로 알린다."
            : "상주 프로그램이 꺼져 있다 — 저장하면 띄운다. 로그인할 때도 자동으로 뜬다."
        : _shortcutInstalled
            ? "시작 메뉴 「Folder Deck 1회용」 바로가기가 단축키를 받는다 — 상주가 없지만 뜨기까지 몇 초 걸릴 수 있다."
            : "설치본 바로가기가 없어 적용할 수 없다 — 값은 저장되고, 설치본에서 앱을 띄우면 그때 맞춘다.";

    /// <summary>단축키 칸에서 눌린 조합. 규칙 밖이면 값은 그대로 두고 이유만 보인다.</summary>
    public void Capture(bool ctrl, bool alt, bool shift, string keyName)
    {
        if (ScratchHotkey.Compose(ctrl, alt, shift, keyName, out var reason) is { } composed)
        {
            HotkeyText = composed;
            HotkeyMessage = null;
        }
        else
        {
            HotkeyMessage = reason;
        }
    }

    [RelayCommand]
    public void ClearHotkey()
    {
        HotkeyText = ScratchHotkey.None;
        HotkeyMessage = null;
    }
}
