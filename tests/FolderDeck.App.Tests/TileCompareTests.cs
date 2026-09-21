using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 간 비교(기능 후보 ①, 2026-09-18 설계, doc/FeatureCandidates_2026-09-18_design.md
/// §①) — Ctrl+D(<c>FolderPanelViewModel.StartCompareCommand</c>)부터
/// <c>MainViewModel</c>의 배지 적용·해제까지.
/// </summary>
public sealed class TileCompareTests
{
    private static async Task<MainWindowFixture> InitializedFixtureAsync()
    {
        var f = new MainWindowFixture();

        // 비교가 "다름"·"같다"까지 실제로 갈리는지 보려고 두 상시 폴더에 겹치는 이름을 심는다.
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(f.CodePath, "shared-same.txt"), "same content");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-same.txt"), "same content");
        File.SetLastWriteTimeUtc(Path.Combine(f.CodePath, "shared-same.txt"), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(f.OutputPath, "shared-same.txt"), stamp);

        File.WriteAllText(Path.Combine(f.CodePath, "shared-diff.txt"), "a");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared-diff.txt"), "bb"); // 크기가 다르다

        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task ComparingTwoTilesAppliesBadgesToBothSides()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0; // 유일한 다른 타일(산출물)
        code.StartCompareCommand.Execute(null);

        Assert.True(f.ViewModel.IsComparing);
        Assert.Same(f.ViewModel.Tiles[0], f.ViewModel.ComparePeerA);
        Assert.Same(f.ViewModel.Tiles[1], f.ViewModel.ComparePeerB);

        Assert.Equal(
            CompareStatus.Same,
            code.Items.Single(i => i.Name == "shared-same.txt").CompareStatus);
        Assert.Equal(
            CompareStatus.Same,
            output.Items.Single(i => i.Name == "shared-same.txt").CompareStatus);

        Assert.Equal(
            CompareStatus.Different,
            code.Items.Single(i => i.Name == "shared-diff.txt").CompareStatus);

        Assert.Equal(
            CompareStatus.OnlyHere,
            code.Items.Single(i => i.Name == "Main.cs").CompareStatus);
        Assert.Equal(
            CompareStatus.OnlyHere,
            output.Items.Single(i => i.Name == "a.dll").CompareStatus);
    }

    [Fact]
    public async Task OnlyOneCandidateIsOfferedExcludingTheRequestingTileAndEmptyTiles()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);

        // 상시 둘(code·산출물)만 열려 있고, 순환 칸은 InitializeAsync 뒤에도 비어 있다 —
        // 후보는 "산출물" 하나뿐이어야 한다.
        var options = Assert.Single(f.Prompt.PickOptionsAsked);
        Assert.Single(options);
    }

    [Fact]
    public async Task PressingCompareAgainOnAPeerTileClearsThePairing()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);
        Assert.True(f.ViewModel.IsComparing);

        output.StartCompareCommand.Execute(null); // 이미 낀 타일 — 토글 꺼짐

        Assert.False(f.ViewModel.IsComparing);
        Assert.Null(f.ViewModel.ComparePeerA);
        Assert.Null(f.ViewModel.ComparePeerB);
        Assert.All(code.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
        Assert.All(output.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
    }

    [Fact]
    public async Task RefreshingAPeerTileClearsTheStaleComparison()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        f.Prompt.PickAnswer = 0;
        code.StartCompareCommand.Execute(null);
        Assert.True(f.ViewModel.IsComparing);

        await output.RefreshAsync();

        Assert.False(f.ViewModel.IsComparing);
        Assert.All(code.Items, i => Assert.Equal(CompareStatus.Unset, i.CompareStatus));
    }

    [Fact]
    public async Task NoOtherOpenTileWarnsInsteadOfPromptingAnEmptyList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // 산출물 타일을 빼서 열린 타일을 하나(code)만 남긴다.
        f.ViewModel.IsEditingLayout = true;
        f.ViewModel.RemoveTile(f.ViewModel.Tiles[1]);

        var code = f.ViewModel.Panels[0];
        code.StartCompareCommand.Execute(null);

        Assert.Empty(f.Prompt.PickOptionsAsked);
        Assert.False(f.ViewModel.IsComparing);
    }

    /// <summary>
    /// 2026-09-20 JH 손 확인: "Ctrl+D로 순환1을 골랐는데 아무 진행이 없다"는 보고가 있었다.
    /// 여기서는 상시 타일과 순환 타일 사이의 비교가 뷰모델 단에서 실제로 되는지만 본다 —
    /// 위 다른 시험은 전부 상시×상시라 순환 쪽은 덮인 적이 없었다. 결과: 된다(배지가
    /// 정상적으로 걸린다) — 손 확인에서 본 "아무 일도 없음"은 이 자리가 아니라
    /// FolderPanelView.xaml 의 목록 뷰 배지 누락(같은 날 다른 손 확인 항목, 이미 수정)
    /// 쪽이었을 가능성이 높다는 근거다.
    /// </summary>
    [Fact]
    public async Task ComparingAPinnedTileAgainstARotatingTileAlsoAppliesBadges()
    {
        using var f = new MainWindowFixture();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(f.CodePath, "shared.txt"), "same");
        File.WriteAllText(Path.Combine(f.DocsPath, "shared.txt"), "same");
        // 두 File.WriteAllText 가 실제 시각차 없이 정확히 같은 ModifiedUtc 로 남는다는 보장이
        // 없다(파일시스템 타임스탬프 해상도) — 위 InitializedFixtureAsync 와 같은 이유로 못박는다.
        File.SetLastWriteTimeUtc(Path.Combine(f.CodePath, "shared.txt"), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(f.DocsPath, "shared.txt"), stamp);

        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var code = f.ViewModel.Panels[0];
        var rotating = f.Rotating;

        // code 기준 후보는 [산출물(index 0), 순환(index 1)] 순이다 — 순환을 고른다.
        f.Prompt.PickAnswer = 1;
        code.StartCompareCommand.Execute(null);

        Assert.True(f.ViewModel.IsComparing);
        Assert.Equal(
            CompareStatus.Same,
            code.Items.Single(i => i.Name == "shared.txt").CompareStatus);
        Assert.Equal(
            CompareStatus.Same,
            rotating.Items.Single(i => i.Name == "shared.txt").CompareStatus);
    }

    /// <summary>
    /// 2026-09-21 실측 손 확인: 상대 타일을 고르고 나면 상태 바에 "비교 시작"은 뜨는데
    /// 배지가 하나도 안 보인다는 보고가 있었다. 원인은 <c>PickOneDialog</c>가 닫히며
    /// 주 창을 다시 활성화하는 것이 <c>MainWindow.OnActivated</c>의 "창 활성화 시 재조회"
    /// (§8)를 불러, <c>RunCompare</c>가 막 걸어 둔 비교를 <c>LoadCompleted</c>로 곧바로
    /// 지워 버리는 것이었다 — <c>IsOperationRunning</c>이 파일 조작용 대화상자에 거는
    /// 것과 같은 종류의 경쟁이다. 실제 창 활성화(Win32 WM_ACTIVATE 타이밍)는 자동 시험이
    /// 못 재현한다 — 여기서는 <see cref="MainViewModel.IsShowingComparePicker"/>가
    /// <c>PickOneDialog</c>가 떠 있는 동안만(=<c>PickOne</c> 호출 안에서) <c>true</c>이고
    /// 끝나면 <c>false</c>로 돌아오는지만 못박는다 — <c>MainWindow.OnActivated</c>가
    /// 이 값을 읽는 것은 손 확인 몫이다.
    /// </summary>
    [Fact]
    public async Task IsShowingComparePickerIsTrueOnlyWhileThePickerDialogWouldBeOpen()
    {
        using var f = await InitializedFixtureAsync();
        var code = f.ViewModel.Panels[0];

        bool? duringCall = null;
        f.Prompt.OnPickOneCalled = () => duringCall = f.ViewModel.IsShowingComparePicker;
        f.Prompt.PickAnswer = 0;

        Assert.False(f.ViewModel.IsShowingComparePicker);
        code.StartCompareCommand.Execute(null);

        Assert.True(duringCall);
        Assert.False(f.ViewModel.IsShowingComparePicker);
    }
}
