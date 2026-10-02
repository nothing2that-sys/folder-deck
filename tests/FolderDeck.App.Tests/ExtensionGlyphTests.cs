using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;





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





    [Fact]
    public async Task SavingFromTheSettingsDialogRefreshesAlreadyOpenPanelsImmediately()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal("\U0001F4C4", code.Items.Single(i => i.Name == "Main.cs").Glyph);

        f.SettingsEditor.Answer = new SettingsEditResult(
            new Dictionary<string, string> { [".cs"] = "🧩" }, ShowShellIcons: true, EverythingMaxResults: 1000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("🧩", code.Items.Single(i => i.Name == "Main.cs").Glyph);
    }


    [Fact]
    public async Task CancellingTheSettingsDialogLeavesGlyphsUnchanged()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        var code = f.ViewModel.Panels[0];
        Assert.Equal("\U0001F4C4", code.Items.Single(i => i.Name == "Main.cs").Glyph);
        Assert.Single(f.SettingsEditor.Opened);
    }
}
