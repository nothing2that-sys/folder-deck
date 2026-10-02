using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

internal sealed record SpanDisplay(string Text, Brush Background);

internal sealed record ContentDiffRow(
    string LeftNumber, IReadOnlyList<SpanDisplay> LeftSpans, Brush LeftLineBackground,
    string RightNumber, IReadOnlyList<SpanDisplay> RightSpans, Brush RightLineBackground);

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

    private Brush LineBackgroundFor(ContentDiffLineKind kind) => kind switch
    {
        ContentDiffLineKind.Deleted => (Brush)FindResource("Brush.DangerSurface"),
        ContentDiffLineKind.Inserted => (Brush)FindResource("Brush.SuccessSurface"),
        _ => Brushes.Transparent,
    };

    private void OnLeftScrollChanged(object sender, ScrollChangedEventArgs e) =>
        SyncVerticalScroll(RightScroll, e.VerticalOffset);

    private void OnRightScrollChanged(object sender, ScrollChangedEventArgs e) =>
        SyncVerticalScroll(LeftScroll, e.VerticalOffset);

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

    private void OnLeftHeaderClick(object sender, MouseButtonEventArgs e) => _openFile(_leftPath);

    private void OnRightHeaderClick(object sender, MouseButtonEventArgs e) => _openFile(_rightPath);
}
