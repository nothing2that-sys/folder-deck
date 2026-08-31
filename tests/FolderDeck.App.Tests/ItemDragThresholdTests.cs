using System.Windows;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

/// <summary>
/// 항목 드래그의 임계 판정 (§4.2-2 · D71).
/// </summary>
/// <remarks>
/// <see cref="RubberBandTests"/> 의 「임계」 절과 같은 결이다 — 여기서도 실제 마우스 제스처는
/// 흉내 내지 않고 <see cref="ItemDragThreshold.PastThreshold"/> 순수 함수만 문다. 원래
/// <c>OnItemPreviewMouseMove</c> 안에 있던 식을 그대로 뺀 것이라, 판정 값은
/// <see cref="RubberBand.PastThreshold"/> 와 결과가 겹치지만(같은 SystemParameters 를 쓰므로)
/// 뜻이 다른 판정이라 따로 시험한다.
/// </remarks>
public sealed class ItemDragThresholdTests
{
    /// <summary>안 움직였거나 몇 픽셀만 움직인 것은 아직 클릭이다 — 드래그가 아니다.</summary>
    [Fact]
    public void NotMovingFarEnoughIsNotPastTheThreshold()
    {
        var origin = new Point(100, 100);

        Assert.False(ItemDragThreshold.PastThreshold(origin, origin));
        Assert.False(ItemDragThreshold.PastThreshold(origin, new Point(100.5, 100.5)));
    }

    /// <summary>어느 한 축이라도 임계를 넘으면 드래그다.</summary>
    [Fact]
    public void CrossingTheThresholdOnEitherAxisIsPastTheThreshold()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 + h, 100)));
        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100, 100 + v)));

        // 방향은 상관없다.
        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 - h, 100)));
        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100, 100 - v)));
    }

    /// <summary>
    /// 경계값 자체는 <b>이미 넘긴 것</b>이다 — 원본 식이 "미만일 때만 아직 클릭"이라
    /// 정확히 같은 값은 미만이 아니다. 바로 그 아래는 여전히 클릭이다.
    /// </summary>
    [Fact]
    public void TheExactThresholdValueIsAlreadyPastItButOneLessIsNot()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 + h, 100 + v)));
        Assert.False(ItemDragThreshold.PastThreshold(origin, new Point(100 + h - 0.01, 100 + v - 0.01)));
    }
}
