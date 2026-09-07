using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="MotwGuard"/> — MOTW(<c>Zone.Identifier</c>) 판정 수단(Phase 17 · D98).
/// </summary>
public sealed class MotwGuardTests
{
    private static string NewTempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"folderdeck-d98-{Guid.NewGuid():N}.txt");

    [Fact]
    public void FileWithZoneIdentifierIsSkipped()
    {
        var path = NewTempFilePath();
        File.WriteAllText(path, "x");
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

            Assert.True(MotwGuard.ShouldSkipPreviewHandler(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlainFileWithoutTheStreamIsNotSkipped()
    {
        var path = NewTempFilePath();
        File.WriteAllText(path, "x");
        try
        {
            Assert.False(MotwGuard.ShouldSkipPreviewHandler(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingPathIsSkipped()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"folderdeck-d98-missing-{Guid.NewGuid():N}.txt");

        Assert.True(MotwGuard.ShouldSkipPreviewHandler(missing));
    }

    [Fact]
    public void EmptyOrNullPathIsSkipped()
    {
        Assert.True(MotwGuard.ShouldSkipPreviewHandler(string.Empty));
        Assert.True(MotwGuard.ShouldSkipPreviewHandler(null));
    }

    [Fact]
    public void FolderPathIsSkipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"folderdeck-d98-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.True(MotwGuard.ShouldSkipPreviewHandler(dir));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
