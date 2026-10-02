using System.Text.Json;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

public sealed class AppSettingsSchemaTests : IDisposable
{
    private readonly string _root;
    private readonly FolderDeckPaths _paths;
    private readonly WorkspaceStore _store;

    public AppSettingsSchemaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.SettingsTests", Guid.NewGuid().ToString("N"));
        _paths = new FolderDeckPaths(_root);
        _paths.EnsureCreated();
        _store = new WorkspaceStore(_paths, new DebouncedSaveScheduler(TimeSpan.FromMilliseconds(10)));
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {

        }
    }

    private void WriteSettingsFile(string json) => File.WriteAllText(_paths.SettingsFile, json);

    [Fact]
    public void CurrentSchemaVersionIsFour() =>
        Assert.Equal(4, AppSettings.CurrentSchemaVersion);

    [Fact]
    public void TheWorkspaceSchemaMovesForItsOwnReasons() =>
        Assert.Equal(11, Workspace.CurrentSchemaVersion);

    [Fact]
    public void TheTileGapRoundTripsAndTheFileIsStampedWithTheCurrentVersion()
    {
        Assert.Null(_store.SaveSettings(new AppSettings { SkipLauncher = true, TileGap = 4 }));

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(4, loaded.Value!.TileGap);
        Assert.True(loaded.Value.SkipLauncher);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Value.SchemaVersion);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void EveryRampValueSurvivesTheFile(int gap)
    {
        Assert.Null(_store.SaveSettings(new AppSettings { TileGap = gap }));

        Assert.Equal(gap, _store.LoadSettings().Value!.TileGap);
    }

    [Fact]
    public void TheChoicesAreTheFiveRampValues() =>
        Assert.Equal([4, 6, 8, 12, 16], AppSettings.TileGapChoices);

    [Fact]
    public void AV1FileWithNoTileGapSlotReadsAsTheOldSpacing()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 1,
              "skipLauncher": true
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(AppSettings.DefaultTileGap, loaded.Value!.TileGap);
        Assert.Equal(8, loaded.Value.TileGap);
        Assert.True(loaded.Value.SkipLauncher);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-4)]
    public void ATileGapOutsideTheRampReadsAsTheOldSpacing(int gap)
    {
        WriteSettingsFile($$"""
            {
              "schemaVersion": 1,
              "tileGap": {{gap}}
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(8, loaded.Value!.TileGap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-4)]
    public void SettingATileGapOutsideTheRampFallsBackToTheOldSpacing(int gap) =>
        Assert.Equal(8, new AppSettings { TileGap = gap }.TileGap);

    [Fact]
    public void AFreshSettingsObjectStartsAtTheOldSpacing() =>
        Assert.Equal(8, new AppSettings().TileGap);

    [Fact]
    public void SavingAV1FilePromotesItToTheCurrentVersionAndWritesTheSlot()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 1,
              "skipLauncher": true
            }
            """);

        var settings = _store.LoadSettings().Value!;
        settings.TileGap = 12;

        Assert.Null(_store.SaveSettings(settings));

        using var document = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFile));

        Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(12, document.RootElement.GetProperty("tileGap").GetInt32());
    }

    [Fact]
    public void AFileFromANewerVersionIsStillRejected()
    {
        WriteSettingsFile($$"""
            {
              "schemaVersion": {{AppSettings.CurrentSchemaVersion + 1}}
            }
            """);

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, _store.LoadSettings().Failure!.Kind);
    }

    [Fact]
    public void AV3FileWithNoShowShellIconsSlotReadsAsShown()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 3,
              "skipLauncher": true
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.True(loaded.Value!.ShowShellIcons);
    }

    [Fact]
    public void AnExplicitShowShellIconsFalseStaysFalse()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 3,
              "showShellIcons": false
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.False(loaded.Value!.ShowShellIcons);
    }

    [Fact]
    public void AFileWithNoEverythingMaxResultsSlotReadsAsTheDefault()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 3
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(AppSettings.DefaultEverythingMaxResults, loaded.Value!.EverythingMaxResults);
        Assert.Equal(1000, loaded.Value.EverythingMaxResults);
    }

    [Fact]
    public void AnEverythingMaxResultsOutsideTheRampFallsBackToTheDefault()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 3,
              "everythingMaxResults": 777
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(1000, loaded.Value!.EverythingMaxResults);
    }

    [Fact]
    public void AnEverythingMaxResultsOnTheRampSurvives()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 3,
              "everythingMaxResults": 2000
            }
            """);

        var loaded = _store.LoadSettings();

        Assert.Null(loaded.Failure);
        Assert.Equal(2000, loaded.Value!.EverythingMaxResults);
    }

    [Fact]
    public void SavingStampsSchemaVersionFour()
    {
        Assert.Null(_store.SaveSettings(new AppSettings()));

        using var document = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFile));
        Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void ASchemaVersionFiveFileIsRejectedAsUnsupported()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 5
            }
            """);

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, _store.LoadSettings().Failure!.Kind);
    }
}
