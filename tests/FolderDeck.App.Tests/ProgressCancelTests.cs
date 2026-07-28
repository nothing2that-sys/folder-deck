using FolderDeck.App.Services;
using FolderDeck.Core.Models;
using System.ComponentModel;
using System.Diagnostics;

namespace FolderDeck.App.Tests;

/// <summary>진행 표시와 취소 (§1.1 · Phase 3 Stage 3).</summary>
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

    /// <summary>파일이 많은 폴더를 심는다 — 진행이 여러 번 보고될 만큼.</summary>
    private static void FillTree(string root, int count)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(root, $"f{i}.log"), new string('x', 64));
        }
    }

    /// <summary>
    /// 진행 보고는 <see cref="SynchronizationContext"/> 를 거쳐 올라온다 — 실행이 끝난
    /// 직후엔 마지막 보고가 아직 큐에 있을 수 있다. 조건이 설 때까지 잠깐 기다린다.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(15);
        }

        Assert.True(condition(), "진행 보고가 올라오지 않았다.");
    }

    // ── 진행 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 큰 폴더 하나는 단위로 세면 <c>0/1</c> 에 멈춰 보인다 — 실제로 만진 파일 수를 같이 보인다.
    /// </summary>
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

    /// <summary>진행 줄은 조작이 끝나면 스스로 접힌다 — 잔상이 남지 않는다.</summary>
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

    /// <summary>
    /// 눈 깜짝할 사이에 끝난 조작 뒤에 진행 줄이 <b>되살아나면</b> 안 된다.
    /// </summary>
    /// <remarks>
    /// 0.4초 예약이 결과 대화상자(모달) 뒤에서 뒤늦게 터지던 버그다 — 실행은 이미 끝났는데
    /// 다 찬 막대가 다시 떠서 아직 도는 중인 것처럼 보였다. 실행 중에 발견해 고쳤다.
    /// </remarks>
    [Fact]
    public async Task AFinishedOperationDoesNotResurrectTheProgressRow()
    {
        using var f = await ReadyAsync();

        // 건너뜀을 만들어 결과 보고(대화상자) 경로까지 타게 한다.
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: false);

        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        // 예약된 0.4초가 지나도 접혀 있어야 한다.
        await Task.Delay(ProgressRevealGraceMs);

        Assert.False(f.ViewModel.IsProgressVisible);
    }

    /// <summary>뷰모델의 0.4초 예약이 지나갔음을 확실히 하는 여유.</summary>
    private const int ProgressRevealGraceMs = 800;

    // ── 취소 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 취소를 누르면 손대지 않은 것은 그대로 남고, 결과가 "취소됨"이라고 말한다.
    /// <para>
    /// 언제 누르는지를 재현 가능하게 하려고, 조작이 취소 손잡이를 잡은 바로 그 순간
    /// (첫 <c>ProgressText</c> 변경)에 누른다 — 시간에 기대지 않는다.
    /// </para>
    /// </summary>
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

        // 시작도 안 한 단위는 손대지 않았다.
        Assert.False(Directory.Exists(Path.Combine(f.DocsPath, "logs")));
        Assert.Equal(30, Directory.GetFiles(Path.Combine(f.WorkPath, "logs")).Length);
        Assert.False(f.ViewModel.IsOperationRunning);
    }

    /// <summary>
    /// 이동을 취소해도 원본은 통째로 남는다 — 삭제는 복사가 전부 끝난 뒤에만 일어난다.
    /// </summary>
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

    /// <summary>도는 것이 없을 때 취소를 눌러도 아무 일도 없다.</summary>
    [Fact]
    public async Task CancelingWhenNothingRunsIsHarmless()
    {
        using var f = await ReadyAsync();

        f.ViewModel.CancelOperation();

        Assert.Null(f.ViewModel.Message);
        Assert.False(f.ViewModel.IsOperationRunning);
    }
}
