namespace FolderDeck.App.Tests;

/// <summary>
/// 워크스페이스 내 중복 파일 찾기(기능 후보 ②, 2026-09-18 설계,
/// doc/FeatureCandidates_2026-09-18_design.md §②) — <c>MainViewModel.FindDuplicateGroups</c>
/// 가 열린 모든 타일을 실제로 훑는지. 그룹핑 자체(이름+크기 판정)는
/// <c>FolderDeck.Core.Tests.DuplicateFinderTests</c> 가 이미 순수 함수로 덮는다 — 여기서는
/// "여러 타일에 걸쳐 실제로 모아오는가"만 확인한다.
/// </summary>
public sealed class FindDuplicateGroupsTests
{
    [Fact]
    public async Task FindsADuplicateAcrossTwoDifferentOpenTiles()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "shared.bin"), "same size");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared.bin"), "same size");
        await f.ViewModel.InitializeAsync();

        var groups = f.ViewModel.FindDuplicateGroups();

        var group = Assert.Single(groups, g => g.Name == "shared.bin");
        Assert.Equal(2, group.Entries.Count);
        Assert.Contains(group.Entries, e => e.Item.FullPath == Path.Combine(f.CodePath, "shared.bin"));
        Assert.Contains(group.Entries, e => e.Item.FullPath == Path.Combine(f.OutputPath, "shared.bin"));
    }

    [Fact]
    public async Task EachEntryCarriesItsOwnTilesLabelNotTheOthers()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "shared.bin"), "x");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared.bin"), "x");
        await f.ViewModel.InitializeAsync();

        var group = Assert.Single(f.ViewModel.FindDuplicateGroups());

        var codeEntry = group.Entries.Single(e => e.Item.FullPath.StartsWith(f.CodePath, StringComparison.Ordinal));
        var outputEntry = group.Entries.Single(e => e.Item.FullPath.StartsWith(f.OutputPath, StringComparison.Ordinal));
        Assert.NotEqual(codeEntry.TileLabel, outputEntry.TileLabel);
    }

    [Fact]
    public async Task NoOverlapReturnsNoGroups()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // 픽스처 기본 내용(code: Main.cs 등, 산출물: a.dll·b.dll)은 서로 이름이 다 다르다.
        Assert.Empty(f.ViewModel.FindDuplicateGroups());
    }
}
