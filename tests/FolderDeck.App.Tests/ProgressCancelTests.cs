using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using System.ComponentModel;
using System.Diagnostics;

namespace FolderDeck.App.Tests;

public sealed class ProgressCancelTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    private static void FillTree(string root, int count)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(root, $"f{i}.log"), new string('x', 64));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(15);
        }

        Assert.True(condition(), "진행 보고가 올라오지 않았다.");
    }

    [Fact]
    public async Task ProgressCountsUnitsAndActualFilesWhileCopyingAFolder()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 60);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "logs");
        f.ViewModel.AddToTray(f.Row("문서"));

        var seen = new List<string>();
        void OnChanged(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText)
                && f.ViewModel.ProgressText is { } text)
            {
                lock (seen)
                {
                    seen.Add(text);
                }
            }
        }

        f.ViewModel.PropertyChanged += OnChanged;
        try
        {
            await f.ViewModel.CopySelectionAsync();

            await WaitUntilAsync(() =>
            {
                lock (seen)
                {
                    return seen.Any(t => t.Contains("파일"));
                }
            });
        }
        finally
        {
            f.ViewModel.PropertyChanged -= OnChanged;
        }

        lock (seen)
        {
            Assert.Contains(seen, t => t.StartsWith("복사 준비 중", StringComparison.Ordinal));
            Assert.Contains(seen, t => t.Contains("파일") && t.Contains("복사 0/1건"));
        }

        Assert.Equal(60, Directory.GetFiles(Path.Combine(f.DocsPath, "logs")).Length);
    }

    [Fact]
    public async Task TheProgressRowIsGoneWhenTheOperationEnds()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        Assert.False(f.ViewModel.IsProgressVisible);
        Assert.False(f.ViewModel.IsOperationRunning);
    }

    [Fact]
    public async Task AFinishedOperationDoesNotResurrectTheProgressRow()
    {
        using var f = await ReadyAsync();

        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: false);

        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        await Task.Delay(ProgressRevealGraceMs);

        Assert.False(f.ViewModel.IsProgressVisible);
    }

    private const int ProgressRevealGraceMs = 800;

    [Fact]
    public async Task CancelingStopsTheOperationAndSaysSo()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "logs");
        f.ViewModel.AddToTray(f.Row("문서"));

        void CancelAtOnce(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                f.ViewModel.CancelOperation();
            }
        }

        f.ViewModel.PropertyChanged += CancelAtOnce;
        try
        {
            await f.ViewModel.CopySelectionAsync();
        }
        finally
        {
            f.ViewModel.PropertyChanged -= CancelAtOnce;
        }

        Assert.Contains("취소됨", f.ViewModel.Message);

        Assert.False(Directory.Exists(Path.Combine(f.DocsPath, "logs")));
        Assert.Equal(30, Directory.GetFiles(Path.Combine(f.WorkPath, "logs")).Length);
        Assert.False(f.ViewModel.IsOperationRunning);
    }

    [Fact]
    public async Task CancelingAFolderMoveLeavesTheSourceIntact()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "logs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.Prompt.Answer = true;

        void CancelAtOnce(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                f.ViewModel.CancelOperation();
            }
        }

        f.ViewModel.PropertyChanged += CancelAtOnce;
        try
        {
            await f.ViewModel.MoveSelectionAsync();
        }
        finally
        {
            f.ViewModel.PropertyChanged -= CancelAtOnce;
        }

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "logs")));
        Assert.Equal(30, Directory.GetFiles(Path.Combine(f.WorkPath, "logs")).Length);
    }

    [Fact]
    public async Task CancelingWhenNothingRunsIsHarmless()
    {
        using var f = await ReadyAsync();

        f.ViewModel.CancelOperation();

        Assert.Null(f.ViewModel.Message);
        Assert.False(f.ViewModel.IsOperationRunning);
    }
}
