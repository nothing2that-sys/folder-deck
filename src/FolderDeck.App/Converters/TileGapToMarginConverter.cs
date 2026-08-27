using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Converters;

/// <summary>
/// 「타일 사이 간격」 하나를 격자 칸의 <see cref="Thickness"/> 로 (§3.2 · D56).
/// </summary>
/// <remarks>
/// <b>사용자가 고르는 값은 눈에 보이는 간격이지 칸 여백이 아니다.</b> 이웃한 칸 둘이 각자
/// 여백을 내므로 화면에 보이는 틈은 그 <b>두 배</b>다 — 그래서 여백은 고른 값의 <b>절반</b>이다.
/// <para>
/// <b>절반을 계산하는 자리는 여기 하나다.</b> 격자 여백이 걸린 곳은 넷인데(타일 · 셀 층 ·
/// 편집 손잡이 층 · 드래그 미리보기) 네 자리에 각각 <c>/2</c> 를 적으면 하나만 고쳐지는
/// 사고가 난다 — §4.9 가 같은 이유로 경계해 둔 결이다. 어긋나면 드래그 미리보기가 실제
/// 타일과 다른 자리에 그려진다.
/// </para>
/// <para>
/// 격자 둘레는 여백 <b>한 겹</b>이라 자동으로 간격의 절반이 된다 — 지금 비율(8 : 4) 그대로다.
/// </para>
/// </remarks>
public sealed class TileGapToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int gap ? gap / 2d : AppSettings.DefaultTileGap / 2d);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
