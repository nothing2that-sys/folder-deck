using System.Windows;
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

        RowList.ItemsSource = rows;
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
}
