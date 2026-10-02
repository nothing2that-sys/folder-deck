using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

public sealed class RecycleBinTests
{
    private readonly RecycleBin _bin = new();

    [Fact]
    public void AMissingPathComesBackAsAReasonNotAnException()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"folderdeck-no-such-{Guid.NewGuid():N}.txt");

        var reason = _bin.Send(missing);

        Assert.Equal("원본이 없다", reason);
    }

    [Fact]
    public void AUnicodePathIsMarshalledIntact()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"폴더덱 테스트 파일 {Guid.NewGuid():N}.txt");

        var reason = _bin.Send(missing);

        Assert.Equal("원본이 없다", reason);
    }

    [Fact]
    public void AnEmptyPathIsARejectedArgumentNotASilentNoOp()
    {
        Assert.Throws<ArgumentException>(() => _bin.Send("   "));
        Assert.Throws<ArgumentNullException>(() => _bin.Send(null!));
    }
}
