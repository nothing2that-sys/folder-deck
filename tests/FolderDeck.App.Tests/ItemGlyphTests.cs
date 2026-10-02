using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

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

    [Fact]
    public async Task IsDirectoryTellsTheTwoApartForTheStyleTrigger()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.True(code.Items.Single(i => i.Name == "Recipe").IsDirectory);
        Assert.False(code.Items.Single(i => i.Name == "Main.cs").IsDirectory);
    }

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
