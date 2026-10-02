using System.Windows.Media.Imaging;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

public sealed class ShellIconTests
{
    private readonly ShellIconService _service = new();

    private static string MissingTxtPath() =>
        Path.Combine(Path.GetTempPath(), $"folderdeck-shell-icon-missing-{Guid.NewGuid():N}", "missing.txt");

    [Fact]
    public void SameExtensionInDifferentFoldersSharesTheSameKey()
    {
        var keyA = ShellIconService.ComputeCacheKey(@"C:\folder-a\readme.txt", isDirectory: false);
        var keyB = ShellIconService.ComputeCacheKey(@"C:\folder-b\notes.txt", isDirectory: false);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void DifferentFoldersGetDifferentKeys()
    {
        var keyA = ShellIconService.ComputeCacheKey(@"C:\folder-a", isDirectory: true);
        var keyB = ShellIconService.ComputeCacheKey(@"C:\folder-b", isDirectory: true);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void SameNamedExeInDifferentFoldersGetsDifferentKeys()
    {
        var keyA = ShellIconService.ComputeCacheKey(@"C:\folder-a\app.exe", isDirectory: false);
        var keyB = ShellIconService.ComputeCacheKey(@"C:\folder-b\app.exe", isDirectory: false);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void PathKeyedFileNamesDifferAcrossFolders()
    {
        foreach (var fileName in new[] { "shortcut.lnk", "icon.ico", "README" })
        {
            var keyA = ShellIconService.ComputeCacheKey(Path.Combine(@"C:\folder-a", fileName), isDirectory: false);
            var keyB = ShellIconService.ComputeCacheKey(Path.Combine(@"C:\folder-b", fileName), isDirectory: false);

            Assert.NotEqual(keyA, keyB);
        }
    }

    [Fact]
    public async Task MissingPathStillGetsAnIconViaFileAttributes()
    {
        var missing = MissingTxtPath();
        Assert.False(File.Exists(missing));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));

        var icon = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(icon);
    }

    [Fact]
    public async Task TheReturnedImageIsFrozen()
    {
        var missing = MissingTxtPath();

        var icon = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(icon);
        Assert.True(icon!.IsFrozen);
    }

    [Fact]
    public async Task AskingTheSameKeyTwiceHitsTheCache()
    {
        var missing = MissingTxtPath();

        var first = await _service.GetIconAsync(missing, isDirectory: false);
        var second = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ClearMakesTheNextFetchANewInstance()
    {
        var missing = MissingTxtPath();

        var first = await _service.GetIconAsync(missing, isDirectory: false);
        _service.Clear();
        var second = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    private static readonly IntPtr NonZero = new(1);

    [Fact]
    public void ARetriedApiCallIsNeededWhenTheResultSucceedsButTheIconHandleIsZero() =>
        Assert.True(ShellIconService.ShouldRetryFetch(NonZero, IntPtr.Zero));

    [Fact]
    public void AZeroResultIsAlsoRetriedWhenNoIconWasReturned()
    {
        Assert.True(ShellIconService.ShouldRetryFetch(IntPtr.Zero, IntPtr.Zero));
        Assert.False(ShellIconService.ShouldRetryFetch(IntPtr.Zero, NonZero));
    }

    [Fact]
    public void NoRetryWhenTheIconHandleIsAlreadyNonZero() =>
        Assert.False(ShellIconService.ShouldRetryFetch(NonZero, NonZero));

    [Fact]
    public void ANullImageIsNotCached() =>
        Assert.False(ShellIconService.ShouldCache(null));

    [Fact]
    public void AFolderUsesTheGenericDirectoryShellIconAfterItsPathLookupFails()
    {
        Assert.True(ShellIconService.ShouldUseDirectoryFallback(
            isDirectory: true, useFileAttributes: false, iconWasReturned: false));
        Assert.False(ShellIconService.ShouldUseDirectoryFallback(
            isDirectory: false, useFileAttributes: false, iconWasReturned: false));
        Assert.False(ShellIconService.ShouldUseDirectoryFallback(
            isDirectory: true, useFileAttributes: true, iconWasReturned: false));
        Assert.False(ShellIconService.ShouldUseDirectoryFallback(
            isDirectory: true, useFileAttributes: false, iconWasReturned: true));
    }

    [Fact]
    public async Task ANonNullImageSatisfiesTheCachePredicate()
    {
        var missing = MissingTxtPath();
        var icon = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(icon);
        Assert.True(ShellIconService.ShouldCache(icon));
    }

    [Fact]
    public async Task AQueuedRequestCanBeCancelledBeforeItCallsTheShell()
    {
        using var releaseFirst = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;
        using var gate = new SemaphoreSlim(1, 1);

        BitmapSource? BlockingFetch(string path, bool useFileAttributes, bool isDirectory)
        {
            Interlocked.Increment(ref fetchCount);
            firstStarted.TrySetResult();
            releaseFirst.Wait(TimeSpan.FromSeconds(10));
            return null;
        }

        var service = new ShellIconService(gate, BlockingFetch);
        var firstPath = Path.Combine(@"C:\folder-a", "first.aaa");
        var secondPath = Path.Combine(@"C:\folder-b", "second.bbb");

        var first = service.GetIconAsync(firstPath, isDirectory: false);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var queued = service.GetIconAsync(secondPath, isDirectory: false, cts.Token);
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
}
