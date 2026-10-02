using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

public sealed class ShellThumbnailTests
{

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

    [Fact]
    public async Task AQueuedLargeIconRequestCanBeCancelledBeforeItCallsTheShell()
    {
        var firstFolder = Directory.CreateTempSubdirectory("FolderDeck-first-icon-");
        var secondFolder = Directory.CreateTempSubdirectory("FolderDeck-second-icon-");
        using var releaseFirst = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;
        using var gate = new SemaphoreSlim(1, 1);

        BitmapSource? BlockingFetch(string path, int width, int height, bool thumbnailOnly)
        {
            Interlocked.Increment(ref fetchCount);
            firstStarted.TrySetResult();
            releaseFirst.Wait(TimeSpan.FromSeconds(10));
            return null;
        }

        try
        {
            var service = new ShellThumbnailService(
                cacheCapacity: 4, fetch: BlockingFetch, fetchGate: gate);
            var first = service.GetShellVisualAsync(firstFolder.FullName, true, 76, 76);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var cts = new CancellationTokenSource();
            var queued = service.GetShellVisualAsync(secondFolder.FullName, true, 76, 76, cts.Token);
            cts.Cancel();

            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
                Assert.Equal(1, Volatile.Read(ref fetchCount));
            }
            finally
            {
                releaseFirst.Set();
                await first;
            }
        }
        finally
        {
            firstFolder.Delete(recursive: true);
            secondFolder.Delete(recursive: true);
        }
    }
}
