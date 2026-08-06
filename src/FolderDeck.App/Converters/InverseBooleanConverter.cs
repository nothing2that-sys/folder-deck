using System.Globalization;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

/// <summary>
/// <c>true</c> → <c>false</c>. 조건이 서 있는 동안 컨트롤을 잠그는 <c>IsEnabled</c> 바인딩에 쓴다.
/// </summary>
/// <remarks>
/// Style 트리거로도 되지만 <b>로컬 값이 트리거를 이기는 함정</b>이 있어
/// (<see cref="InverseBoolToVisibilityConverter"/> 가 같은 이유로 만들어졌다)
/// 바인딩 한 줄로 끝낸다.
/// </remarks>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
