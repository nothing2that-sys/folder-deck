using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

/// <summary>
/// 목록 항목의 글리프 — 어휘표 그대로 (Phase 8-C ①).
/// </summary>
/// <remarks>
/// Phase 8-B ③ 이 폴더를 <c>▣</c> 로 바꿨다가 여기서 되돌렸다. <b>이 판은 되돌린 상태를
/// 고정하는 것이 목적이다</b> — <c>doc/FolderDeck_familylook_spec.md</c> §6 어휘표가 폴더
/// <c>📁</c> · 파일 <c>📄</c> 를 못박고 있고, 갈라 보이게 하는 일은 이제 글리프가 아니라
/// 색이 맡는다(<c>Views/FolderPanelView.xaml</c> 의 <c>ItemGlyph</c> 스타일 ·
/// <c>Brush.FolderGlyph</c> / <c>Brush.FileGlyph</c>).
/// <para>
/// 색은 XAML 스타일이라 여기서 못 읽는다(테스트 어셈블리는 MTA 라 WPF 컨트롤을 못 만든다).
/// 이 판이 지키는 것은 <b>색이 갈라 붙을 근거</b>인 <see cref="FileItemViewModel.IsDirectory"/>
/// 가 항목마다 제대로 선다는 것까지다.
/// </para>
/// </remarks>
public sealed class ItemGlyphTests
{
    [Fact]
    public async Task FoldersAndFilesUseTheVocabularyGlyphs()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        var folder = code.Items.Single(i => i.Name == "Recipe");
        var file = code.Items.Single(i => i.Name == "Main.cs");

        Assert.Equal("\U0001F4C1", folder.Glyph);
        Assert.Equal("\U0001F4C4", file.Glyph);
    }

    /// <summary>
    /// 매핑이 비어 있으면 지금(v1)과 같다 — 두 갈래뿐이다(D59 결정 6). <b>확장자별로 절대
    /// 늘지 않는다는 뜻이 아니다</b> — <see cref="ExtensionGlyphTests"/> 가 매핑이 찼을 때를
    /// 다룬다. 이 판은 이름을 바꿨다(원래 <c>ThereAreExactlyTwoGlyphs</c>) — D59 전에는
    /// "확장자별로 늘리지 않는다"는 항상-참인 규약이었지만 D59 가 그 규약을 뒤집었고, 지금은
    /// "매핑이 비어 있을 때만" 참인 좁은 사실이기 때문이다.
    /// </summary>
    [Fact]
    public async Task WithNoExtensionMappingThereAreExactlyTwoGlyphs()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var glyphs = f.ViewModel.Panels
            .SelectMany(p => p.Items)
            .Select(i => i.Glyph)
            .Distinct()
            .ToList();

        Assert.All(glyphs, g => Assert.Contains(g, new[] { "\U0001F4C1", "\U0001F4C4" }));
    }

    /// <summary>색을 갈라 붙일 근거 — 스타일 트리거가 읽는 값이다.</summary>
    [Fact]
    public async Task IsDirectoryTellsTheTwoApartForTheStyleTrigger()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.True(code.Items.Single(i => i.Name == "Recipe").IsDirectory);
        Assert.False(code.Items.Single(i => i.Name == "Main.cs").IsDirectory);
    }

    /// <summary>
    /// 상세 뷰의 크기 칸은 폴더만 비어 있다 — <b>색을 못 봐도</b> 종류를 읽을 수 있다는 근거이고,
    /// "색만으로 구분하지 않는다"가 여기서 깨지지 않는 이유다.
    /// </summary>
    [Fact]
    public async Task TheSizeColumnAlsoTellsFoldersApart()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Equal(string.Empty, code.Items.Single(i => i.Name == "Recipe").SizeText);
        Assert.NotEqual(string.Empty, code.Items.Single(i => i.Name == "Main.cs").SizeText);
    }
}
