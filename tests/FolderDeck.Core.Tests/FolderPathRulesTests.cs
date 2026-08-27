using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 이름 규칙의 유일한 자리 (§4.11-1 D4). <see cref="FolderPathRules.RejectLeafName"/> 는
/// <b>사람이 새로 적은 이름</b>을 보는 쪽이고 A군 ②의 「이름 바꾸기」가 이것 하나만 부른다.
/// </summary>
public sealed class FolderPathRulesTests
{
    [Theory]
    [InlineData("Recipe")]
    [InlineData("Main.cs")]
    [InlineData(".gitignore")]
    [InlineData("한글 이름")]
    [InlineData("a.b.c")]
    [InlineData("CONSOLE")]        // 예약 이름의 접두사일 뿐이다
    [InlineData("COM10")]          // COM1~9 만 예약이다
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

    /// <summary>구분자도 금칙 문자에 들어 있다 — 하위 경로를 적어 폴더를 옮기지 못한다.</summary>
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

    /// <summary>제어문자는 그대로 찍으면 안 보이므로 코드로 보여 준다.</summary>
    [Fact]
    public void ShowsControlCharactersAsCodes() =>
        Assert.Contains("\\u0001", FolderPathRules.RejectLeafName("a\u0001b"));

    /// <summary>
    /// Windows 는 끝의 점·공백을 말없이 깎는다 — 조용히 다듬으면 화면과 디스크가 어긋난다.
    /// </summary>
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

    /// <summary>확장자를 붙여도 예약이다.</summary>
    [Theory]
    [InlineData("CON.txt")]
    [InlineData("aux.log.bak")]
    public void RejectsReservedNamesWithExtensions(string name) =>
        Assert.Contains("예약 이름", FolderPathRules.RejectLeafName(name));

    // ── 기존 둘이 그대로인지 (한 곳에 모인 규칙의 나머지) ─────────────────────

    /// <summary>이름 바꾸기가 <c>item.FullPath</c> 를 이걸로 다듬은 뒤 부모를 뗀다.</summary>
    [Fact]
    public void NormalizeDropsTheTrailingSeparator() =>
        Assert.Equal(@"D:\code", FolderPathRules.Normalize(@"D:\code\"));

    [Fact]
    public void LeafNameTakesTheLastSegment() =>
        Assert.Equal("Recipe", FolderPathRules.LeafName(@"D:\code\Recipe\"));

    // ── 볼륨 판정 (§4.2-2 · D51 결정 4) ───────────────────────────────────────
    //
    // <b>실제 볼륨을 만들지 않는다.</b> 순수 함수라 경로 문자열만으로 시험할 수 있고, 그것이
    // 엔진의 private static 에서 여기로 나온 이유다 — 두 번째 드라이브도 NAS 도 없는 기계에서
    // 「D: 에서 NAS 로 끌면 복사」라는 규칙을 시험할 수 있어야 한다.

    [Fact]
    public void SameRootIsTheSameVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"D:\output"));

    [Fact]
    public void ADifferentDriveLetterIsADifferentVolume() =>
        Assert.False(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"C:\output"));

    /// <summary>드라이브 문자의 대소문자는 볼륨을 가르지 않는다 — 껐다 켜면 어느 쪽으로도 온다.</summary>
    [Fact]
    public void TheDriveLetterCaseDoesNotMatter() =>
        Assert.True(FolderPathRules.SameVolume(@"d:\code", @"D:\output"));

    /// <summary>UNC 는 <b>서버가 아니라 공유</b>가 루트다 — 같은 서버의 다른 공유는 다른 볼륨이다.</summary>
    [Fact]
    public void TheSameUncShareIsTheSameVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"\\nas\backup\a.txt", @"\\nas\backup\old"));

    [Fact]
    public void ADifferentUncShareIsADifferentVolume() =>
        Assert.False(FolderPathRules.SameVolume(@"\\nas\backup\a.txt", @"\\nas\media"));

    /// <summary>로컬 드라이브와 UNC 공유는 언제나 다른 볼륨이다 — 이 회차가 겨냥한 바로 그 경로다.</summary>
    [Fact]
    public void ALocalDriveAndAUncShareAreDifferentVolumes() =>
        Assert.False(FolderPathRules.SameVolume(@"D:\code\Main.cs", @"\\nas\backup"));

    /// <summary>
    /// 상대 경로도 <b>먼저 정규화한 뒤</b> 본다 — 안 그러면 루트가 빈 문자열로 나와 아무 상대 경로나
    /// 서로 같은 볼륨이 된다.
    /// </summary>
    [Fact]
    public void ARelativePathIsResolvedBeforeTheRootIsRead()
    {
        var here = Directory.GetCurrentDirectory();

        Assert.True(FolderPathRules.SameVolume("sub", here));
        Assert.True(FolderPathRules.SameVolume(@"sub\deeper", "other"));
    }

    /// <summary>끝 구분자는 루트를 바꾸지 않는다 — 목적지 경로가 어느 쪽으로도 들어온다.</summary>
    [Fact]
    public void ATrailingSeparatorDoesNotChangeTheVolume() =>
        Assert.True(FolderPathRules.SameVolume(@"D:\code\", @"D:\code"));
}
