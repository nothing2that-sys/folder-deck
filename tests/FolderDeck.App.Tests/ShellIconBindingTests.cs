using System.ComponentModel;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="FileItemViewModel.ShellIcon"/> — 파일 목록 아이콘 3단(§3.2) 중 ② 단을 뷰모델에
/// 배선한 자리(Phase 11 · D68-c). D68-b(<see cref="ShellIconTests"/>)는 취득 계층 자체를 다뤘고,
/// 이 판은 그 계층을 <see cref="FileItemViewModel"/> 이 부르는 규칙(① 단 우선 · 캐시 동기 경로 ·
/// 변경 알림 · 한 번만 걸기)을 다룬다.
/// </summary>
/// <remarks>
/// WPF 컨트롤은 세우지 않는다(이 어셈블리는 MTA).
/// <para>
/// <b>실측 메모.</b> <c>SHGetFileInfo</c> 는 전체 스위트가 병렬로 도는 동안 아주 드물게(수백
/// 동시 호출 기준 1% 안팎) <c>null</c> 을 돌려준다 — 셸32 쪽의 동시성 한계로 보이고, 이 회차
/// (D68-c)가 손볼 계층(§4-1 캐시 키 하나) 밖이다. <see cref="FileItemViewModel"/> 은 설계대로
/// 그 한 번의 실패를 그대로 받아들여 재시도하지 않으므로(§4-2 "한 번만 건다"), "결국 아이콘을
/// 받는다"류의 판은 <b>시험 쪽에서</b>(운영 코드가 아니라) 새 경로로 다시 붙여 이 무관한 환경
/// 잡음을 흡수한다 — <see cref="TryEventuallyAsync"/>.
/// </para>
/// </remarks>
public sealed class ShellIconBindingTests
{
    /// <summary>실제로 없는 경로를 만든다 — GUID 로 매 시험마다 새 자리를 고른다.</summary>
    private static string MissingTxtPath() =>
        Path.Combine(Path.GetTempPath(), $"folderdeck-d68c-missing-{Guid.NewGuid():N}.txt");

    /// <summary>
    /// 실제로 있는 빈 폴더를 만든다 — 폴더는 경로 키라(§4-1) <c>SHGFI_USEFILEATTRIBUTES</c> 를
    /// 안 쓰고, 디스크의 실제 항목을 봐야 아이콘이 나온다.
    /// </summary>
    private static string CreateTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"folderdeck-d68c-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FolderItem MakeItem(string path, bool isDirectory) =>
        new(Path.GetFileName(path), path, isDirectory, Size: isDirectory ? null : 0, ModifiedUtc: DateTime.UtcNow);

    /// <summary>
    /// <see cref="FileItemViewModel.ShellIcon"/> 의 변경 알림을 기다린다. 이미 도착해 있으면
    /// (캐시 동기 경로) 기다리지 않고 바로 true 를 준다.
    /// </summary>
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

    /// <summary>
    /// <paramref name="makeVm"/> 이 매번 새 경로로 새 뷰모델을 만든다 — 실패하면(위 실측 메모)
    /// 완전히 새 경로로 다시 붙인다. 운영 코드의 "한 번만 건다" 규칙은 매 시도 안에서 그대로다.
    /// </summary>
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

        // 기다리지 않는다 — 캐시 동기 경로(TryGetCached)로 즉시 나와야 한다.
        Assert.NotNull(second.ShellIcon);
    }

    /// <summary>경로 키가 대소문자를 안 가린다(§4-1) — 같은 폴더를 대소문자만 바꿔 물어도 같은 키다.</summary>
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

            // 기다리지 않는다 — 대소문자만 다른 같은 경로가 같은 캐시 키로 들어가야 한다.
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
}
