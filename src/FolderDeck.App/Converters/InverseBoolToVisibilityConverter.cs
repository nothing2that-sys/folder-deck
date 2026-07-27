using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

/// <summary>
/// <c>true</c> → <see cref="Visibility.Collapsed"/>. 접힘 플래그로 본문을 숨길 때 쓴다.
/// </summary>
/// <remarks>
/// 내장 <c>BooleanToVisibilityConverter</c> 는 반대 방향만 되고 파라미터로 뒤집을 수 없다.
/// Style 트리거로도 되지만, 로컬 <c>Visibility</c> 값이 트리거를 이기는 함정이 있어
/// 바인딩 한 줄로 끝내는 편이 안전하다.
/// </remarks>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
