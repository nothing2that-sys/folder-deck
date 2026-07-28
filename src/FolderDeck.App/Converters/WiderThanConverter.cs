using System;
using System.Globalization;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

/// <summary>
/// <c>ActualWidth</c> 가 파라미터로 준 폭보다 넓은가.
/// </summary>
/// <remarks>
/// 좁은 타일에서 헤더 아이콘을 접는 데 쓴다. 1x1 타일은 작은 창에서 140px 도 안 되는데
/// 내비 아이콘 6개만 144px 라 그대로 두면 잘린다. 대체 조작(마우스 뒤로/앞으로 버튼,
/// <c>Alt+←/→</c>, <c>F5</c>)이 있는 것부터 접는다.
/// </remarks>
public sealed class WiderThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width))
        {
            return true;
        }

        var threshold = parameter is string text
                        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0d;

        return width > threshold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
