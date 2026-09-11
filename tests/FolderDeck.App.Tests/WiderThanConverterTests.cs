using System.Globalization;
using FolderDeck.App.Converters;

namespace FolderDeck.App.Tests;

/// <summary>
/// 헤더가 아이콘 열 개/셋 중 어느 쪽으로 갈릴지를 결정하는 컨버터(Phase 20 · D120 ·
/// W1~W2, 임계 290 — 유도 과정은 <c>App.xaml</c> 의 <c>WideOnlyNavButton</c> 주석
/// 참고). 새 파일이다 — 이 저장소에 컨버터를 직접 무는 시험이 없었다.
/// </summary>
public sealed class WiderThanConverterTests
{
    private const string Threshold = "290";

    [Fact]
    public void BoundaryValuesResolveCorrectly()
    {
        var converter = new WiderThanConverter();

        // 임계 미만(289) — 명백히 좁다. false(WideOnlyNavButton 은 접힘/셋 쪽).
        Assert.False((bool)converter.Convert(289.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);

        // 임계와 같음(290) — WiderThanConverter 는 엄격한 ">" 비교라 여기서도 false 다.
        // D120 지시서 §3 의 "임계 이상은 펼침"이라는 문구는 이 경계를 ">="로 읽은 것으로
        // 보이지만, §4-2 가 새 컨버터를 만들지 말고 재사용하라고 못박았고 실제
        // WiderThanConverter.Convert 는 `width > threshold` 다(줄 29) — 재사용하는
        // 컨버터의 실제 식을 기준으로 삼는다.
        Assert.False((bool)converter.Convert(290.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);

        // 임계 초과(291) — 명백히 넓다. true(WideOnlyNavButton 은 펼침/열 개 쪽).
        Assert.True((bool)converter.Convert(291.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);
    }
}
