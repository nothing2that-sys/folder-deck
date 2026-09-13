using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// 파일 목록 미리보기 폭포(§3.2) 중 <b>② 단</b>(셸 썸네일) 캐시 계층(Phase 17 · D86 · 회차 2b-1).
/// 취득(셸 호출)은 손 확인으로 남고, 여기서는 <see cref="ShellThumbnailService"/> 의 도장
/// (stamp) 계산과 64칸 LRU 캐시 규칙만 문다 — <see cref="ShellIconService"/> 의 캐시 시험과 같은 결이다.
/// </summary>
public sealed class ShellThumbnailTests
{
    /// <summary>작게 만든 1x1 이미지 — 셸을 부르지 않는다.</summary>
    private static BitmapSource MakeImage()
    {
        var pixels = new byte[] { 0, 0, 0, 0 };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void TheStampChangesWhenTheFileChanges()
    {
        var modified = DateTime.UtcNow;
        var before = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 100, modified);
        var after = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 200, modified);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void AskingTheSameStampTwiceHitsTheCache()
    {
        var service = new ShellThumbnailService();
        var stamp = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 100, DateTime.UtcNow);
        var image = MakeImage();

        service.Store(stamp, image);

        Assert.True(service.TryGetCached(stamp, out var cached));
        Assert.Same(image, cached);
    }

    [Fact]
    public void AskingADifferentStampMissesTheCache()
    {
        var service = new ShellThumbnailService();
        var stamp = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 100, DateTime.UtcNow);
        service.Store(stamp, MakeImage());

        var other = ShellThumbnailService.ComputeStamp(@"D:\x\b.jpg", 100, DateTime.UtcNow);

        Assert.False(service.TryGetCached(other, out _));
    }

    [Fact]
    public void ANullImageIsNotCached()
    {
        var service = new ShellThumbnailService();
        var stamp = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 100, DateTime.UtcNow);

        service.Store(stamp, null);

        Assert.False(service.TryGetCached(stamp, out _));
    }

    [Fact]
    public void TheLeastRecentlyUsedEntryIsEvictedAtCapacity()
    {
        var service = new ShellThumbnailService(cacheCapacity: 2);
        var first = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 1, DateTime.UnixEpoch);
        var second = ShellThumbnailService.ComputeStamp(@"D:\x\b.jpg", 1, DateTime.UnixEpoch);
        var third = ShellThumbnailService.ComputeStamp(@"D:\x\c.jpg", 1, DateTime.UnixEpoch);

        service.Store(first, MakeImage());
        service.Store(second, MakeImage());
        Assert.True(service.TryGetCached(first, out _));

        service.Store(third, MakeImage());

        Assert.True(service.TryGetCached(first, out _));
        Assert.False(service.TryGetCached(second, out _));
        Assert.True(service.TryGetCached(third, out _));
    }

    [Fact]
    public void ANewerImageReplacesTheSameStampWithoutGrowingTheCache()
    {
        var service = new ShellThumbnailService(cacheCapacity: 1);
        var stamp = ShellThumbnailService.ComputeStamp(@"D:\x\a.jpg", 1, DateTime.UnixEpoch);
        var first = MakeImage();
        var second = MakeImage();

        service.Store(stamp, first);
        service.Store(stamp, second);

        Assert.True(service.TryGetCached(stamp, out var cached));
        Assert.Same(second, cached);
    }

    [Fact]
    public async Task AMissingFileYieldsNoThumbnail()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var service = new ShellThumbnailService();

        var result = await service.GetThumbnailAsync(path, 64, 64);

        Assert.Null(result);
    }

    [Fact]
    public async Task ARealPngYieldsAFrozenLargeShellVisual()
    {
        var root = Directory.CreateTempSubdirectory("FolderDeck-thumbnail-");
        var path = Path.Combine(root.FullName, "sample.png");

        try
        {
            var pixels = Enumerable.Repeat((byte)0x7f, 256 * 256 * 4).ToArray();
            var bitmap = BitmapSource.Create(
                256, 256, 96, 96, PixelFormats.Bgra32, null, pixels, 256 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            var service = new ShellThumbnailService();
            var result = await service.GetShellVisualAsync(path, false, 76, 76);

            Assert.NotNull(result);
            Assert.True(result.IsFrozen);
            Assert.True(result.PixelWidth >= 64, $"Shell visual width was only {result.PixelWidth}px.");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ARealFolderYieldsAFrozenLargeShellVisual()
    {
        var root = Directory.CreateTempSubdirectory("FolderDeck-folder-icon-");

        try
        {
            var service = new ShellThumbnailService();
            var result = await service.GetShellVisualAsync(root.FullName, true, 76, 76);

            Assert.NotNull(result);
            Assert.True(result.IsFrozen);
            Assert.True(result.PixelWidth >= 64, $"Shell visual width was only {result.PixelWidth}px.");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
