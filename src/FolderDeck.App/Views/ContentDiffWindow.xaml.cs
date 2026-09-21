using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

/// <summary>줄 안의 한 토막 — 뷰가 바로 바인딩하는 표시용 DTO.</summary>
internal sealed record SpanDisplay(string Text, Brush Background);

/// <summary>내용 비교 결과 한 줄(양쪽) — 뷰가 바로 바인딩하는 표시용 DTO.</summary>
internal sealed record ContentDiffRow(
    string LeftNumber, IReadOnlyList<SpanDisplay> LeftSpans, Brush LeftLineBackground,
    string RightNumber, IReadOnlyList<SpanDisplay> RightSpans, Brush RightLineBackground);

/// <summary>
/// 내용 비교 결과 창(기능 후보 ①의 연장, 2026-09-18). 비모달 — <c>Show()</c>로 연다.
/// </summary>
/// <remarks>
/// 2026-09-21 JH 재지적으로 화면을 다시 짰다 — "창이 두 개여야 할듯. 세로 스크롤은
/// 공유해도, 가로 스크롤은 따로." 왼쪽/오른쪽을 각자의 <see cref="ScrollViewer"/>로
/// 두고(가로는 <c>Auto</c>, 세로는 <c>Auto</c>) <see cref="OnLeftScrollChanged"/>·
/// <see cref="OnRightScrollChanged"/>로 세로 오프셋만 서로 맞춘다 — 가로 오프셋은
/// 건드리지 않으므로 한쪽이 옆으로 길게 스크롤해도 반대쪽은 그대로다. 위쪽엔 각자
/// 실제 경로를 보여준다(전에는 타일 이름만 있어 "왼쪽이 무슨 파일인지" 헷갈렸다) — 누르면
/// 그 파일이 열린다(<see cref="OnLeftHeaderClick"/>). 가운데 <c>GridSplitter</c>로 폭을
/// 조절할 수 있다.
/// </remarks>
public partial class ContentDiffWindow : Window
{
    private bool _syncingScroll;
    private readonly string _leftPath;
    private readonly string _rightPath;
    private readonly Action<string> _openFile;

    public ContentDiffWindow(
        string fileName, string leftLabel, string leftPath, string rightLabel, string rightPath,
        ContentDiffResult diff, Action<string> openFile)
    {
        InitializeComponent();

        _leftPath = leftPath;
        _rightPath = rightPath;
        _openFile = openFile;

        Title = $"내용 비교 — {fileName}";
        LeftHeaderText.Text = $"{leftLabel} — {leftPath}";
        LeftHeaderText.ToolTip = $"{leftPath}\n눌러서 이 파일 열기";
        RightHeaderText.Text = $"{rightLabel} — {rightPath}";
        RightHeaderText.ToolTip = $"{rightPath}\n눌러서 이 파일 열기";

        bool hasDifferences = diff.Left.Any(l => l.Kind is not (ContentDiffLineKind.Unchanged or ContentDiffLineKind.Imaginary))
            || diff.Right.Any(l => l.Kind is not (ContentDiffLineKind.Unchanged or ContentDiffLineKind.Imaginary));

        SummaryText.Text = hasDifferences
            ? $"'{fileName}' — 내용이 다르다."
            : $"'{fileName}' — 내용은 같다. 크기·수정 시각 등 메타데이터만 다른 것으로 보인다.";

        var changedSpanBrush = (Brush)FindResource("Brush.WarningSurface");

        var rows = new List<ContentDiffRow>(diff.Left.Count);
        for (int i = 0; i < diff.Left.Count; i++)
        {
            var left = diff.Left[i];
            var right = diff.Right[i];
            rows.Add(new ContentDiffRow(
                LeftNumber: left.LineNumber?.ToString() ?? string.Empty,
                LeftSpans: SpansFor(left, changedSpanBrush),
                LeftLineBackground: LineBackgroundFor(left.Kind),
                RightNumber: right.LineNumber?.ToString() ?? string.Empty,
                RightSpans: SpansFor(right, changedSpanBrush),
                RightLineBackground: LineBackgroundFor(right.Kind)));
        }

        LeftList.ItemsSource = rows;
        RightList.ItemsSource = rows;
    }

    /// <summary>
    /// 바뀐 줄(Modified)은 이제 줄 전체를 노랗게 칠하지 않는다 — 실제로 바뀐 단어만 골라
    /// 칠한다(2026-09-21 JH 지적: "노란색으로만 표시되면 뭐가 다른지 확인이 안 된다").
    /// 그 외 종류는 토막을 안 나눈다 — 줄 전체가 지워졌거나/생겼거나/같은 것이라 나눌
    /// 이유가 없다, 한 토막(전체 텍스트, 안 칠함)으로 낸다.
    /// </summary>
    private static IReadOnlyList<SpanDisplay> SpansFor(ContentDiffLine line, Brush changedBrush)
    {
        if (line.Kind == ContentDiffLineKind.Modified && line.Spans is { Count: > 0 })
        {
            return line.Spans
                .Select(span => new SpanDisplay(span.Text, span.IsChanged ? changedBrush : Brushes.Transparent))
                .ToList();
        }

        return [new SpanDisplay(line.Text ?? string.Empty, Brushes.Transparent)];
    }

    /// <summary>
    /// 줄 전체 배경 — 완전히 지워지거나(Deleted) 생긴(Inserted) 줄만 칠한다. Modified 는
    /// 이제 줄 배경이 아니라 <see cref="SpansFor"/>가 낸 토막별 배경으로 표시한다.
    /// </summary>
    private Brush LineBackgroundFor(ContentDiffLineKind kind) => kind switch
    {
        ContentDiffLineKind.Deleted => (Brush)FindResource("Brush.DangerSurface"),
        ContentDiffLineKind.Inserted => (Brush)FindResource("Brush.SuccessSurface"),
        _ => Brushes.Transparent,
    };

    /// <summary>왼쪽이 세로로 스크롤되면 오른쪽도 같은 오프셋으로 맞춘다 — 가로는 안 건드린다.</summary>
    private void OnLeftScrollChanged(object sender, ScrollChangedEventArgs e) =>
        SyncVerticalScroll(RightScroll, e.VerticalOffset);

    private void OnRightScrollChanged(object sender, ScrollChangedEventArgs e) =>
        SyncVerticalScroll(LeftScroll, e.VerticalOffset);

    /// <summary>
    /// <c>_syncingScroll</c> 재진입 방지 — 안 걸면 A→B, B→A 가 서로 부르며 되돈다
    /// (프로그램으로 <c>ScrollToVerticalOffset</c> 를 부르는 것도 <c>ScrollChanged</c> 를
    /// 다시 쏘기 때문이다).
    /// </summary>
    private void SyncVerticalScroll(ScrollViewer target, double verticalOffset)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            target.ScrollToVerticalOffset(verticalOffset);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>
    /// 경로 헤더를 누르면 그 파일을 OS 기본 프로그램으로 연다(2026-09-21 JH 요청) — 파일
    /// 목록 더블클릭이 쓰는 것과 같은 통로(<c>IShellLauncher.Open</c>)다. 헤더는 목록
    /// 항목이 아니라 하나뿐인 정적 글자라 더블클릭을 요구할 이유가 없어 한 번 누르면
    /// 바로 연다 — 커서를 손 모양으로, 마우스를 올리면 밑줄이 붙게 해 눌러도 되는
    /// 자리라는 걸 알려준다(XAML의 <c>LeftHeaderText</c>/<c>RightHeaderText</c> 스타일).
    /// </summary>
    private void OnLeftHeaderClick(object sender, MouseButtonEventArgs e) => _openFile(_leftPath);

    private void OnRightHeaderClick(object sender, MouseButtonEventArgs e) => _openFile(_rightPath);
}
