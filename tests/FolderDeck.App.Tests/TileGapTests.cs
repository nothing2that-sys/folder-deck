using System.Text.Json;
using System.Windows;
using FolderDeck.App.Converters;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class TileGapTests
{
    private static readonly TileGapToMarginConverter Converter = new();

    private static Thickness Margin(object? gap) =>
        (Thickness)Converter.Convert(gap!, typeof(Thickness), null!, null!);

    [Theory]
    [InlineData(4, 2d)]
    [InlineData(6, 3d)]
    [InlineData(8, 4d)]
    [InlineData(12, 6d)]
    [InlineData(16, 8d)]
    public void TheConverterHalvesTheGap(int gap, double expected)
    {
        var margin = Margin(gap);

        Assert.Equal(expected, margin.Left);
        Assert.Equal(expected, margin.Top);
        Assert.Equal(expected, margin.Right);
        Assert.Equal(expected, margin.Bottom);
    }

    [Fact]
    public void TheDefaultGapStillGivesTheOldFourPixelMargin() =>
        Assert.Equal(new Thickness(4), Margin(AppSettings.DefaultTileGap));

    [Theory]
    [InlineData(null)]
    [InlineData("8")]
    public void AnUnusableValueFallsBackToTheOldSpacing(object? value) =>
        Assert.Equal(new Thickness(4), Margin(value));

    [Fact]
    public void EveryChoiceLandsOnTheFamilyRamp()
    {
        int[] ramp = [2, 3, 4, 6, 8];

        Assert.Equal(ramp, AppSettings.TileGapChoices.Select(g => (int)Margin(g).Left));
    }

    [Fact]
    public void AWindowWithNoSettingsStartsAtTheOldSpacing()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(8, f.ViewModel.TileGap);
    }

    [Fact]
    public void AWindowStartsAtTheSavedGap()
    {
        using var f = new MainWindowFixture(settings: new AppSettings { TileGap = 4 });

        Assert.Equal(4, f.ViewModel.TileGap);
    }

    [Fact]
    public void TheViewModelOffersTheSameFiveChoices()
    {
        using var f = new MainWindowFixture();

        Assert.Same(AppSettings.TileGapChoices, f.ViewModel.TileGapChoices);
    }

    [Fact]
    public void ChoosingAGapWritesItThroughTheDebouncedSave()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);

        f.ViewModel.TileGap = 12;

        Assert.Equal(12, settings.TileGap);

        f.Store.Flush();

        using var document = JsonDocument.Parse(File.ReadAllText(f.Paths.SettingsFile));

        Assert.Equal(12, document.RootElement.GetProperty("tileGap").GetInt32());
        Assert.Equal(AppSettings.CurrentSchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void AReadOnlySettingsRunChangesTheScreenButNotTheFile()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings, settingsWritable: false);

        f.ViewModel.TileGap = 16;
        f.Store.Flush();

        Assert.Equal(16, f.ViewModel.TileGap);
        Assert.False(File.Exists(f.Paths.SettingsFile));
    }
}
