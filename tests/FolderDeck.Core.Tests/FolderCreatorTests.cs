using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 폴더 하나 만들기 (§4.6 · D75-b). 이름 문자 규칙 자체는 <see cref="FolderPathRulesTests"/> 가
/// 이미 촘촘히 무니, 여기서는 <see cref="FolderCreator.Create"/> 고유의 것만 —
/// 실제 생성 · 이미 있는 이름 · 부모 없음 · (이름 규칙 재사용의) 거절 두 갈래.
/// </summary>
public sealed class FolderCreatorTests : IDisposable
{
    private readonly string _root;

    public FolderCreatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.CreateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 무시.
        }
    }

    // 1
    [Fact]
    public void CreatesTheFolder()
    {
        var reason = FolderCreator.Create(_root, "새 폴더");

        Assert.Null(reason);
        Assert.True(Directory.Exists(Path.Combine(_root, "새 폴더")));
    }

    // 2
    [Fact]
    public void RejectsAnExistingNameAndLeavesItsContentAlone()
    {
        var existing = Directory.CreateDirectory(Path.Combine(_root, "새 폴더")).FullName;
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "원본");

        var reason = FolderCreator.Create(_root, "새 폴더");

        Assert.Contains("이미 있는 이름", reason);
        Assert.Equal("원본", File.ReadAllText(Path.Combine(existing, "keep.txt")));
    }

    // 3
    [Fact]
    public void RejectsBlankNames()
    {
        Assert.Contains("빈 이름", FolderCreator.Create(_root, ""));
        Assert.Contains("빈 이름", FolderCreator.Create(_root, "   "));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    // 4
    [Fact]
    public void RejectsForbiddenCharactersAndSeparators()
    {
        Assert.Contains("쓸 수 없는 문자", FolderCreator.Create(_root, "a/b"));
        Assert.Contains("쓸 수 없는 문자", FolderCreator.Create(_root, "a\\b"));
        Assert.Contains("쓸 수 없는 문자", FolderCreator.Create(_root, "a*b"));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    // 5
    [Fact]
    public void RejectsAMissingParent()
    {
        var missingParent = Path.Combine(_root, "없는-부모");

        var reason = FolderCreator.Create(missingParent, "새 폴더");

        Assert.Contains("부모 폴더가 없다", reason);
        Assert.False(Directory.Exists(missingParent));
    }
}
