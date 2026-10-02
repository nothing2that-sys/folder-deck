using System.Globalization;
using FolderDeck.App.Converters;

namespace FolderDeck.App.Tests;

public sealed class WiderThanConverterTests
{
    private const string Threshold = "290";

    [Fact]
    public void BoundaryValuesResolveCorrectly()
    {
        var converter = new WiderThanConverter();

        Assert.False((bool)converter.Convert(289.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);

        Assert.False((bool)converter.Convert(290.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);

        Assert.True((bool)converter.Convert(291.0, typeof(bool), Threshold, CultureInfo.InvariantCulture)!);
    }
}
