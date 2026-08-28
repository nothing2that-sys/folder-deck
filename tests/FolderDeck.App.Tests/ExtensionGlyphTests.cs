using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 확장자별 글리프 매핑이 실제 파일 목록에 반영되는지 (D59). <see cref="ItemGlyphTests"/> 는
/// 매핑이 비어 있을 때(§1.2 어휘표 그대로)를 지키고, 이 판은 매핑이 채워졌을 때를 다룬다.
/// </summary>
public sealed class ExtensionGlyphTests
{
    [Fact]
    public async Task MappedExtensionGetsItsGlyph()
    {
        var settings = new AppSettings { ExtensionGlyphs = new Dictionary<string, string> { [".cs"] = "🧩" } };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var file = code.Items.Single(i => i.Name == "Main.cs");

        Assert.Equal("🧩", file.Glyph);
    }

    /// <summary>매핑에 없는 확장자는 다른 확장자가 매핑돼 있어도 그대로 📄 다.</summary>
    [Fact]
    public async Task UnmappedExtensionStaysDefaultFileGlyph()
    {
        var settings = new AppSettings { ExtensionGlyphs = new Dictionary<string, string> { [".cs"] = "🧩" } };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels.Single(p => p.Entry?.DisplayName == "산출물");
        var dll = output.Items.Single(i => i.Name == "a.dll");

        Assert.Equal("\U0001F4C4", dll.Glyph);
    }

    /// <summary>폴더는 매핑에 걸리지 않는다(D59 결정 7) — 이름이 매핑된 확장자처럼 보여도 📁 다.</summary>
    [Fact]
    public async Task FoldersIgnoreTheMappingEvenWhenTheirNameLooksLikeAMappedExtension()
    {
        var settings = new AppSettings { ExtensionGlyphs = new Dictionary<string, string> { [".zip"] = "🗜" } };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Directory.CreateDirectory(Path.Combine(f.CodePath, "archive.zip"));
        await code.RefreshAsync();

        var folder = code.Items.Single(i => i.Name == "archive.zip");

        Assert.True(folder.IsDirectory);
        Assert.Equal("\U0001F4C1", folder.Glyph);
    }

    /// <summary>
    /// 대소문자가 달라도 걸린다(D59 결정 5) — 매핑 키(<c>.cs</c>, 정규화된 형태)와 파일의
    /// 확장자 대소문자가 달라도 같은 것으로 본다.
    /// </summary>
    [Fact]
    public async Task MatchingIgnoresCase()
    {
        var settings = new AppSettings { ExtensionGlyphs = new Dictionary<string, string> { [".cs"] = "🧩" } };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        File.WriteAllText(Path.Combine(f.CodePath, "Upper.CS"), "x");
        await code.RefreshAsync();

        var file = code.Items.Single(i => i.Name == "Upper.CS");

        Assert.Equal("🧩", file.Glyph);
    }

    /// <summary>
    /// 설정 창에서 저장하면 이미 열려 있는 패널에도 곧바로 반영된다(D59 「만들 것」) —
    /// F5 없이도 손 확인 ②가 기대하는 그대로다.
    /// </summary>
    [Fact]
    public async Task SavingFromTheSettingsDialogRefreshesAlreadyOpenPanelsImmediately()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal("\U0001F4C4", code.Items.Single(i => i.Name == "Main.cs").Glyph);

        f.IconSettingsEditor.Answer = new Dictionary<string, string> { [".cs"] = "🧩" };
        await f.ViewModel.OpenIconSettingsCommand.ExecuteAsync(null);

        Assert.Equal("🧩", code.Items.Single(i => i.Name == "Main.cs").Glyph);
    }

    /// <summary>취소하면 아무것도 안 바뀐다 — 창이 뜬 것만 기록에 남는다.</summary>
    [Fact]
    public async Task CancellingTheSettingsDialogLeavesGlyphsUnchanged()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.OpenIconSettingsCommand.ExecuteAsync(null);

        var code = f.ViewModel.Panels[0];
        Assert.Equal("\U0001F4C4", code.Items.Single(i => i.Name == "Main.cs").Glyph);
        Assert.Single(f.IconSettingsEditor.Opened);
    }
}
