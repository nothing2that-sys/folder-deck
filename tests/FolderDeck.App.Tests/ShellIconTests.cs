using System.Windows.Media.Imaging;
using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="ShellIconService"/> — 파일 목록 아이콘 3단(§3.2) 중 ② 단(Phase 11 · D68-b).
/// </summary>
/// <remarks>
/// WPF 컨트롤은 세우지 않는다(이 어셈블리는 MTA). <c>SHGetFileInfo</c> 는 순수 shell32 export라
/// <c>IContextMenu</c>(COM) 같은 STA 제약이 없다 — D67 이 이 어셈블리 안에서 1765×2·25×2 회
/// 불러 실측했다.
/// </remarks>
public sealed class ShellIconTests
{
    private readonly ShellIconService _service = new();

    /// <summary>실제로 없는 경로를 만든다 — GUID 로 매 시험마다 새 자리를 고른다.</summary>
    private static string MissingTxtPath() =>
        Path.Combine(Path.GetTempPath(), $"folderdeck-d68b-missing-{Guid.NewGuid():N}", "missing.txt");

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

    /// <summary>
    /// <c>.lnk</c>·<c>.ico</c>·확장자 없는 파일은 확장자로 뭉치지 않는다 — 폴더가 다르면
    /// 키도 다르다(경로 키를 받는다는 뜻. 확장자 키였다면 1번 시험처럼 같은 키가 나왔을 것).
    /// 지시서 표(§5-3)의 4번 판 하나를 이 메서드 하나로 문다 — 시험 개수를 표대로 여덟에 맞춘다.
    /// </summary>
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

    // ── 다시 불러야 하는가 · 캐시에 넣어야 하는가 (D74 — D73 이 잰 산발 실패를 고친다) ──

    private static readonly IntPtr NonZero = new(1);

    /// <summary>D73 Q2 실측의 7/7 — 반환값은 성공인데 hIcon 만 0이면 다시 불러야 한다.</summary>
    [Fact]
    public void ARetriedApiCallIsNeededWhenTheResultSucceedsButTheIconHandleIsZero() =>
        Assert.True(ShellIconService.ShouldRetryFetch(NonZero, IntPtr.Zero));

    /// <summary>
    /// 과거 D73 계측에는 없었지만 전체 시험과 실제 목록에서 다시 관측된 빈 결과도 한 번은
    /// 다시 부른다. 이 길을 놓치면 FileItemViewModel 이 그 행을 세션 동안 글리프로 고정한다.
    /// </summary>
    [Fact]
    public void AZeroResultIsAlsoRetriedWhenNoIconWasReturned()
    {
        Assert.True(ShellIconService.ShouldRetryFetch(IntPtr.Zero, IntPtr.Zero));
        Assert.False(ShellIconService.ShouldRetryFetch(IntPtr.Zero, NonZero));
    }

    /// <summary>hIcon 이 이미 있으면(0이 아니면) 다시 부를 이유가 없다.</summary>
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

    /// <summary>
    /// 이름 정정(검토 회차 발견) — 예전 이름 <c>ANonNullImageIsCached</c> 는 실제로 캐시에
    /// 들어갔는지가 아니라 <see cref="ShellIconService.ShouldCache"/> 판정 함수 하나만
    /// 확인하고 있었다. 실제 캐시 적중은 <see cref="AskingTheSameKeyTwiceHitsTheCache"/> 가
    /// 검증한다 — 이 시험은 그 판정 함수가 실제로 얻은 이미지를 앞에 두고도 옳게 동작하는지만
    /// 본다.
    /// </summary>
    [Fact]
    public async Task ANonNullImageSatisfiesTheCachePredicate()
    {
        var missing = MissingTxtPath();
        var icon = await _service.GetIconAsync(missing, isDirectory: false);

        Assert.NotNull(icon);
        Assert.True(ShellIconService.ShouldCache(icon));
    }

    // ── head-of-line blocking 완화(2026-09-18 프로브 §1) — 대기 중 취소 ──

    /// <summary>
    /// <see cref="ShellThumbnailTests.AQueuedLargeIconRequestCanBeCancelledBeforeItCallsTheShell"/>
    /// 와 같은 자리 — 게이트를 잡고 있는 첫 요청 뒤에 선 두 번째 요청을 대기 중에 취소하면,
    /// 그 요청은 네이티브 호출까지 가지 않고 <see cref="OperationCanceledException"/> 으로
    /// 끝나야 한다(스크롤로 지나간 행이 뒤를 막지 않게 하는 자리).
    /// </summary>
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
