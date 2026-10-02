using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Tests;





public sealed class FolderPathRulesTests
{
    [Theory]
    [InlineData("Recipe")]
    [InlineData("Main.cs")]
    [InlineData(".gitignore")]
    [InlineData("한글 이름")]
    [InlineData("a.b.c")]
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    public void AcceptsOrdinaryNames(string name) =>
        Assert.Null(FolderPathRules.RejectLeafName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RejectsBlank(string? name) =>
        Assert.Contains("빈 이름", FolderPathRules.RejectLeafName(name));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void RejectsDotNames(string name) =>
        Assert.Contains("이름이 될 수 없다", FolderPathRules.RejectLeafName(name));


    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void RejectsForbiddenCharacters(string name) =>
        Assert.Contains("쓸 수 없는 문자", FolderPathRules.RejectLeafName(name));


    [Fact]
    public void ShowsControlCharactersAsCodes() =>
        Assert.Contains("\\u0001", FolderPathRules.RejectLeafName("a\u0001b"));




    [Theory]
    [InlineData("Recipe.")]
    [InlineData("Recipe ")]
    public void RejectsTrailingDotOrSpace(string name) =>
        Assert.Contains("점이나 공백", FolderPathRules.RejectLeafName(name));

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    public void RejectsReservedNames(string name) =>
        Assert.Contains("예약 이름", FolderPathRules.RejectLeafName(name));


    [Theory]
    [InlineData("CON.txt")]
    [InlineData("aux.log.bak")]
    public void RejectsReservedNamesWithExtensions(string name) =>
        Assert.Contains("예약 이름", FolderPathRules.RejectLeafName(name));




    [Fact]
    public void NormalizeDropsTheTrailingSeparator() =>
        Assert.Equal(@"D:\code", FolderPathRules.Normalize(@"D:\code\"));

    [Fact]
    public void LeafNameTakesTheLastSegment() =>
        Assert.Equal("Recipe", FolderPathRules.LeafName(@"D:\code\Recipe\"));







    [Fact]
    public void SameRootIsTheSameVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"D:\output"));

    [Fact]
    public void ADifferentDriveLetterIsADifferentVolume() =>
        Assert.False(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"C:\output"));


    [Fact]
    public void TheDriveLetterCaseDoesNotMatter() =>
        Assert.True(FolderPathRules.SameVolume(@"d:\code", @"D:\output"));


    [Fact]
    public void TheSameUncShareIsTheSameVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"\\nas\backup\a.txt", @"\\nas\backup\old"));

    [Fact]
    public void ADifferentUncShareIsADifferentVolume() =>
        Assert.False(FolderPathRules.SameVolume(@"\\nas\backup\a.txt", @"\\nas\media"));


    [Fact]
    public void ALocalDriveAndAUncShareAreDifferentVolumes() =>
        Assert.False(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"\\nas\backup"));





    [Fact]
    public void ARelativePathIsResolvedBeforeTheRootIsRead()
    {
        var here = Directory.GetCurrentDirectory();

        Assert.True(FolderPathRules.SameVolume("sub", here));
        Assert.True(FolderPathRules.SameVolume(@"sub\deeper", "other"));
    }


    [Fact]
    public void ATrailingSeparatorDoesNotChangeTheVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"D:\code\", @"D:\code"));
}
