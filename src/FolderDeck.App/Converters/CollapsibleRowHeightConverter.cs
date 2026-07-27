using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

/// <summary>
/// 접힘 상태를 <see cref="GridLength"/> 로. 접히면 <c>Auto</c>(헤더 높이만),
/// 펴지면 <c>ConverterParameter</c> 로 준 길이(예: <c>2*</c>).
/// </summary>
/// <remarks>
/// <see cref="RowDefinition"/> 은 <c>FrameworkContentElement</c> 라 바인딩이 걸린다.
/// 본문만 숨기면 star 로 잡힌 행이 빈 공간을 계속 차지하므로 높이까지 같이 바꿔야 한다.
/// </remarks>
public sealed class CollapsibleRowHeightConverter : IValueConverter
{
    private static readonly GridLengthConverter LengthConverter = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return GridLength.Auto;
        }

        if (parameter is string text
            && LengthConverter.ConvertFromString(text) is GridLength expanded)
        {
            return expanded;
        }

        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
