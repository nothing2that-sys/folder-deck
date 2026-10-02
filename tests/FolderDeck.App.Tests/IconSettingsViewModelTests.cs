namespace FolderDeck.App.Tests;





public sealed class IconSettingsViewModelTests
{
    [Fact]
    public void StartsWithOneRowPerExistingMapping()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string> { [".pdf"] = "📕" }, showShellIcons: true);

        var row = Assert.Single(vm.Rows);
        Assert.Equal(".pdf", row.Extension);
        Assert.Equal("📕", row.Glyph);
    }

    [Fact]
    public void AddRowAddsAnEmptyRow()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>(), showShellIcons: true);

        vm.AddRow();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(string.Empty, row.Extension);
        Assert.Equal(string.Empty, row.Glyph);
    }

    [Fact]
    public void RemoveRowTakesItOutOfTheList()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string> { [".pdf"] = "📕" }, showShellIcons: true);
        var row = vm.Rows[0];

        vm.RemoveRow(row);

        Assert.Empty(vm.Rows);
    }


    [Fact]
    public void ToDictionaryNormalizesKeysAndSkipsBlankRows()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>(), showShellIcons: true);
        vm.Rows.Add(new IconMappingRowViewModel { Extension = "PDF", Glyph = "📕" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = "  ", Glyph = "🗑" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".zip", Glyph = "  " });

        var result = vm.ToDictionary();

        var pair = Assert.Single(result);
        Assert.Equal(".pdf", pair.Key);
        Assert.Equal("📕", pair.Value);
    }


    [Fact]
    public void ToDictionaryLetsTheLaterRowWinOnDuplicateKeys()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>(), showShellIcons: true);
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".pdf", Glyph = "🥇" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".PDF", Glyph = "🥈" });

        var result = vm.ToDictionary();

        var pair = Assert.Single(result);
        Assert.Equal(".pdf", pair.Key);
        Assert.Equal("🥈", pair.Value);
    }
}
