using System.Windows;
using System.Windows.Media;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Views;

/// <summary>내용 비교 결과 한 줄(양쪽) — 뷰가 바로 바인딩하는 표시용 DTO.</summary>
internal sealed record ContentDiffRow(
    string LeftNumber, string LeftText, Brush LeftBackground,
    string RightNumber, string RightText, Brush RightBackground);

/// <summary>
/// 내용 비교 결과 창(기능 후보 ①의 연장, 2026-09-18). 비모달 — <c>Show()</c>로 연다.
/// </summary>
public partial class ContentDiffWindow : Window
{
    public ContentDiffWindow(string fileName, string leftLabel, string rightLabel, ContentDiffResult diff)
    {
        InitializeComponent();

        Title = $"내용 비교 — {fileName}";
        LeftHeaderText.Text = leftLabel;
        RightHeaderText.Text = rightLabel;

        bool hasDifferences = diff.Left.Any(l => l.Kind is not (ContentDiffLineKind.Unchanged or ContentDiffLineKind.Imaginary))
            || diff.Right.Any(l => l.Kind is not (ContentDiffLineKind.Unchanged or ContentDiffLineKind.Imaginary));

        SummaryText.Text = hasDifferences
            ? $"'{fileName}' — 내용이 다르다."
            : $"'{fileName}' — 내용은 같다. 크기·수정 시각 등 메타데이터만 다른 것으로 보인다.";

        var rows = new List<ContentDiffRow>(diff.Left.Count);
        for (int i = 0; i < diff.Left.Count; i++)
        {
            var left = diff.Left[i];
            var right = diff.Right[i];
            rows.Add(new ContentDiffRow(
                LeftNumber: left.LineNumber?.ToString() ?? string.Empty,
                LeftText: left.Text ?? string.Empty,
                LeftBackground: BackgroundFor(left.Kind),
                RightNumber: right.LineNumber?.ToString() ?? string.Empty,
                RightText: right.Text ?? string.Empty,
                RightBackground: BackgroundFor(right.Kind)));
        }

        RowList.ItemsSource = rows;
    }

    private Brush BackgroundFor(ContentDiffLineKind kind) => kind switch
    {
        ContentDiffLineKind.Deleted => (Brush)FindResource("Brush.DangerSurface"),
        ContentDiffLineKind.Inserted => (Brush)FindResource("Brush.SuccessSurface"),
        ContentDiffLineKind.Modified => (Brush)FindResource("Brush.WarningSurface"),
        _ => Brushes.Transparent,
    };
}
