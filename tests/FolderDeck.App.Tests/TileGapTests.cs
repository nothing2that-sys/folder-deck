using System.Text.Json;
using System.Windows;
using FolderDeck.App.Converters;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 사이 여백 (§3.2 · D56) — 고르는 값은 <b>눈에 보이는 간격</b>이고 칸 여백이 아니다.
/// </summary>
/// <remarks>
/// 격자 여백이 걸린 자리는 넷이다(타일 · 셀 층 · 편집 손잡이 층 · 드래그 미리보기).
/// 넷이 <b>같은 값 하나</b>를 읽고, <b>절반을 계산하는 자리는 컨버터 하나</b>다 — 네 자리에
/// 각각 <c>/2</c> 를 적으면 하나만 고쳐져 드래그 미리보기가 실제 타일과 다른 자리에 그려진다.
/// <para>
/// XAML 배선(네 자리에 바인딩이 실제로 붙었는가)은 시험이 물지 못한다 — 시험 어셈블리가
/// MTA 라 WPF 컨트롤을 세울 수 없다(<c>design.md</c> §8). 그 이음매는 손 확인이 닫는다.
/// </para>
/// </remarks>
public sealed class TileGapTests
{
    private static readonly TileGapToMarginConverter Converter = new();

    private static Thickness Margin(object? gap) =>
        (Thickness)Converter.Convert(gap!, typeof(Thickness), null!, null!);

    // ── 컨버터 — 절반을 세는 유일한 자리 (결정 6) ────────────────────────────

    /// <summary>
    /// 이웃한 칸 둘이 각자 여백을 내므로 <b>보이는 틈은 두 배</b>다 — 그래서 여백은 절반이다.
    /// <c>6 → 3</c> 이 <b>홀수 여백이 나오는 유일한 자리</b>라 반드시 든다.
    /// </summary>
    /// <remarks>
    /// <b>다섯 값이 전부 짝수라</b> 정수 나눗셈(<c>gap / 2</c>)도 같은 답을 낸다 — 컨버터의
    /// <c>2d</c> 는 램프 밖 값이 새어 들어올 때를 위한 방어이고 <b>이 판이 그것을 잡지는
    /// 못한다.</b> 램프를 지키는 자리가 <see cref="AppSettings.TileGap"/> 하나라 새어 들어올
    /// 길이 지금은 없다.
    /// </remarks>
    [Theory]
    [InlineData(4, 2d)]
    [InlineData(6, 3d)]     // ← 다섯 중 유일한 홀수 여백
    [InlineData(8, 4d)]
    [InlineData(12, 6d)]
    [InlineData(16, 8d)]
    public void TheConverterHalvesTheGap(int gap, double expected)
    {
        var margin = Margin(gap);

        Assert.Equal(expected, margin.Left);
        Assert.Equal(expected, margin.Top);
        Assert.Equal(expected, margin.Right);
        Assert.Equal(expected, margin.Bottom);
    }

    /// <summary>지금 기본값 8 은 이 회차 전의 칸 여백 4 와 같다 — 화면이 안 움직인다.</summary>
    [Fact]
    public void TheDefaultGapStillGivesTheOldFourPixelMargin() =>
        Assert.Equal(new Thickness(4), Margin(AppSettings.DefaultTileGap));

    /// <summary>값이 아직 안 왔거나 엉뚱한 것이 와도 지금 화면으로 떨어진다.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("8")]
    public void AnUnusableValueFallsBackToTheOldSpacing(object? value) =>
        Assert.Equal(new Thickness(4), Margin(value));

    /// <summary>다섯 값이 전부 램프(<c>4/6/8/12/16</c>) 안의 여백을 낸다 — 새 숫자를 만들지 않는다.</summary>
    [Fact]
    public void EveryChoiceLandsOnTheFamilyRamp()
    {
        int[] ramp = [2, 3, 4, 6, 8];

        Assert.Equal(ramp, AppSettings.TileGapChoices.Select(g => (int)Margin(g).Left));
    }

    // ── 뷰모델 — 격자 넷이 읽는 값 하나 ──────────────────────────────────────

    /// <summary>설정을 안 넘긴 창은 8 로 선다 — D56 이전과 같은 화면이다.</summary>
    [Fact]
    public void AWindowWithNoSettingsStartsAtTheOldSpacing()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(8, f.ViewModel.TileGap);
    }

    /// <summary>저장된 값을 들고 뜬다 — 껐다 켜도 고른 값이 남는 자리의 절반이다.</summary>
    [Fact]
    public void AWindowStartsAtTheSavedGap()
    {
        using var f = new MainWindowFixture(settings: new AppSettings { TileGap = 4 });

        Assert.Equal(4, f.ViewModel.TileGap);
    }

    /// <summary>고를 수 있는 목록은 <see cref="AppSettings"/> 것 그대로다 — 두 벌이 아니다.</summary>
    [Fact]
    public void TheViewModelOffersTheSameFiveChoices()
    {
        using var f = new MainWindowFixture();

        Assert.Same(AppSettings.TileGapChoices, f.ViewModel.TileGapChoices);
    }

    /// <summary>
    /// 고르면 설정에 실리고 <b>디바운스 저장</b>으로 파일까지 간다 — 프로덕션이 쓰는 통로
    /// (<c>SaveSettingsDebounced</c>) 그대로다.
    /// </summary>
    [Fact]
    public void ChoosingAGapWritesItThroughTheDebouncedSave()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);

        f.ViewModel.TileGap = 12;

        Assert.Equal(12, settings.TileGap);

        // 밀린 저장을 마무리시킨다 — 앱이 닫힐 때 하는 것과 같다 (§5).
        f.Store.Flush();

        using var document = JsonDocument.Parse(File.ReadAllText(f.Paths.SettingsFile));

        Assert.Equal(12, document.RootElement.GetProperty("tileGap").GetInt32());
        Assert.Equal(AppSettings.CurrentSchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    /// <summary>
    /// 설정 파일이 손상돼 <b>덮어쓰지 않기로 한 실행</b>에서는 파일을 건드리지 않는다 —
    /// 화면은 바뀌지만 조용한 초기화는 하지 않는다(§5 · <c>App.LoadSettings</c>).
    /// </summary>
    [Fact]
    public void AReadOnlySettingsRunChangesTheScreenButNotTheFile()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings, settingsWritable: false);

        f.ViewModel.TileGap = 16;
        f.Store.Flush();

        Assert.Equal(16, f.ViewModel.TileGap);
        Assert.False(File.Exists(f.Paths.SettingsFile));
    }
}
