using System.ComponentModel;
using System.Windows.Media.Imaging;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

public sealed class ShellIconBindingTests
{

    private static string MissingTxtPath() =>
        Path.Combine(Path.GetTempPath(), $"folderdeck-icon-binding-missing-{Guid.NewGuid():N}.txt");

    private static string CreateTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"folderdeck-icon-binding-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FolderItem MakeItem(string path, bool isDirectory) =>
        new(Path.GetFileName(path), path, isDirectory, Size: isDirectory ? null : 0, ModifiedUtc: DateTime.UtcNow);

    private static async Task<bool> WaitForShellIconAsync(FileItemViewModel vm, TimeSpan timeout)
    {
        if (vm.ShellIcon is not null)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileItemViewModel.ShellIcon))
            {
                tcs.TrySetResult(true);
            }
        }

        vm.PropertyChanged += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(true);
            return completed == tcs.Task;
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }
    }

    private static async Task<FileItemViewModel?> TryEventuallyAsync(
        Func<FileItemViewModel> makeVm, int maxAttempts = 3)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var vm = makeVm();
            if (await WaitForShellIconAsync(vm, TimeSpan.FromSeconds(10)))
            {
                return vm;
            }
        }

        return null;
    }

    [Fact]
    public async Task WithNoServiceTheIconStaysNullForever()
    {
        var vm = new FileItemViewModel(MakeItem(MissingTxtPath(), isDirectory: false));

        Assert.Null(vm.ShellIcon);

        await Task.Delay(200);

        Assert.Null(vm.ShellIcon);
    }

    [Fact]
    public async Task ExtensionMappingWinsSoShellIsNeverCalled()
    {
        var glyphs = new Dictionary<string, string> { [".txt"] = "X" };
        var vm = new FileItemViewModel(
            MakeItem(MissingTxtPath(), isDirectory: false),
            extensionGlyphs: glyphs,
            shellIconService: new ShellIconService());

        var arrived = await WaitForShellIconAsync(vm, TimeSpan.FromMilliseconds(300));

        Assert.False(arrived);
        Assert.Null(vm.ShellIcon);
    }

    [Fact]
    public async Task UnmappedFileEventuallyGetsAnIcon()
    {
        var vm = await TryEventuallyAsync(() =>
            new FileItemViewModel(MakeItem(MissingTxtPath(), isDirectory: false), shellIconService: new ShellIconService()));

        Assert.NotNull(vm);
        Assert.NotNull(vm!.ShellIcon);
    }

    [Fact]
    public async Task FoldersAlsoGetAnIcon()
    {
        var dirs = new List<string>();
        try
        {
            var vm = await TryEventuallyAsync(() =>
            {
                var dir = CreateTempFolder();
                dirs.Add(dir);
                return new FileItemViewModel(MakeItem(dir, isDirectory: true), shellIconService: new ShellIconService());
            });

            Assert.NotNull(vm);
            Assert.NotNull(vm!.ShellIcon);
        }
        finally
        {
            foreach (var dir in dirs)
            {
                Directory.Delete(dir);
            }
        }
    }

    [Fact]
    public async Task WhenTheIconArrivesItRaisesAChangeNotificationForTheDisplayProperty()
    {
        List<string?> raised = [];

        var vm = await TryEventuallyAsync(() =>
        {
            raised = [];
            var log = raised;
            var candidate = new FileItemViewModel(
                MakeItem(MissingTxtPath(), isDirectory: false), shellIconService: new ShellIconService());
            candidate.PropertyChanged += (_, e) => log.Add(e.PropertyName);
            return candidate;
        });

        Assert.NotNull(vm);
        Assert.Contains(nameof(FileItemViewModel.ShellIcon), raised);
    }

    [Fact]
    public async Task ASecondItemWithTheSameExtensionGetsTheIconWithoutWaiting()
    {
        var service = new ShellIconService();

        var first = await TryEventuallyAsync(() =>
            new FileItemViewModel(MakeItem(MissingTxtPath(), isDirectory: false), shellIconService: service));
        Assert.NotNull(first);

        var second = new FileItemViewModel(MakeItem(MissingTxtPath(), isDirectory: false), shellIconService: service);

        Assert.NotNull(second.ShellIcon);
    }

    [Fact]
    public async Task PathKeyIgnoresCase()
    {
        var service = new ShellIconService();
        var dirs = new List<string>();
        try
        {
            var first = await TryEventuallyAsync(() =>
            {
                var dir = CreateTempFolder();
                dirs.Add(dir);
                return new FileItemViewModel(MakeItem(dir.ToLowerInvariant(), isDirectory: true), shellIconService: service);
            });
            Assert.NotNull(first);

            var winningDir = dirs[^1];
            var second = new FileItemViewModel(
                MakeItem(winningDir.ToUpperInvariant(), isDirectory: true), shellIconService: service);

            Assert.NotNull(second.ShellIcon);
        }
        finally
        {
            foreach (var dir in dirs)
            {
                Directory.Delete(dir);
            }
        }
    }

    [Fact]
    public async Task ACancelledFetchResetsTheRequestFlagSoALaterViewCanRetry()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        var fetchCount = 0;

        BitmapSource? ControlledFetch(string path, bool useFileAttributes, bool isDirectory)
        {
            if (Interlocked.Increment(ref fetchCount) == 1)
            {
                firstStarted.TrySetResult();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
            else
            {
                secondStarted.TrySetResult();
            }

            return null;
        }

        using var gate = new SemaphoreSlim(1, 1);
        var service = new ShellIconService(gate, ControlledFetch);
        var vm = new FileItemViewModel(MakeItem(MissingTxtPath(), isDirectory: false), shellIconService: service);

        _ = vm.ShellIcon;
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        vm.CancelPendingShellIconFetch();
        releaseFirst.Set();

        for (var attempt = 0; attempt < 200 && !secondStarted.Task.IsCompleted; attempt++)
        {
            _ = vm.ShellIcon;
            await Task.Delay(10);
        }

        Assert.True(secondStarted.Task.IsCompleted, "취소 뒤 재요청이 200회 시도 안에 새 fetch를 걸지 않았다");
        Assert.Equal(2, Volatile.Read(ref fetchCount));
    }
}
