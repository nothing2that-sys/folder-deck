namespace FolderDeck.App.Tests;

/// <summary>
/// 설정 창 「아이콘」 탭의 행 목록 로직 (D59) — 대화상자(WPF Window) 없이 뷰모델만 무는 판.
/// MTA 제약(D52·D55·D58 Q8) 때문에 <c>IconSettingsDialog</c> 자체는 여기서 못 세운다.
/// </summary>
public sealed class IconSettingsViewModelTests
{
    [Fact]
    public void StartsWithOneRowPerExistingMapping()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string> { [".pdf"] = "📕" });

        var row = Assert.Single(vm.Rows);
        Assert.Equal(".pdf", row.Extension);
        Assert.Equal("📕", row.Glyph);
    }

    [Fact]
    public void AddRowAddsAnEmptyRow()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>());

        vm.AddRow();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(string.Empty, row.Extension);
        Assert.Equal(string.Empty, row.Glyph);
    }

    [Fact]
    public void RemoveRowTakesItOutOfTheList()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string> { [".pdf"] = "📕" });
        var row = vm.Rows[0];

        vm.RemoveRow(row);

        Assert.Empty(vm.Rows);
    }

    /// <summary>빈 행(확장자·글리프 중 하나라도 자르고 나서 비면)은 저장되지 않는다.</summary>
    [Fact]
    public void ToDictionaryNormalizesKeysAndSkipsBlankRows()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>());
        vm.Rows.Add(new IconMappingRowViewModel { Extension = "PDF", Glyph = "📕" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = "  ", Glyph = "🗑" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".zip", Glyph = "  " });

        var result = vm.ToDictionary();

        var pair = Assert.Single(result);
        Assert.Equal(".pdf", pair.Key);
        Assert.Equal("📕", pair.Value);
    }

    /// <summary>같은 키로 정규화되는 행이 여럿이면 목록의 나중 행이 이긴다.</summary>
    [Fact]
    public void ToDictionaryLetsTheLaterRowWinOnDuplicateKeys()
    {
        var vm = new IconSettingsViewModel(new Dictionary<string, string>());
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".pdf", Glyph = "🥇" });
        vm.Rows.Add(new IconMappingRowViewModel { Extension = ".PDF", Glyph = "🥈" });

        var result = vm.ToDictionary();

        var pair = Assert.Single(result);
        Assert.Equal(".pdf", pair.Key);
        Assert.Equal("🥈", pair.Value);
    }
}
