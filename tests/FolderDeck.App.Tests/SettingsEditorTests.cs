using System.Text.Json;
using FolderDeck.App.Services;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;







public sealed class SettingsEditorTests
{
    [Fact]
    public async Task CancellingLeavesAllThreeSettingsValuesUnchanged()
    {
        var settings = new AppSettings
        {
            ExtensionGlyphs = new Dictionary<string, string> { [".cs"] = "🧩" },
            ShowShellIcons = true,
            EverythingMaxResults = 2000,
        };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = null;
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("🧩", settings.ExtensionGlyphs[".cs"]);
        Assert.True(settings.ShowShellIcons);
        Assert.Equal(2000, settings.EverythingMaxResults);
    }

    [Fact]
    public async Task SavingUpdatesExtensionGlyphs()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            new Dictionary<string, string> { [".cs"] = "🧩" }, ShowShellIcons: true, EverythingMaxResults: 1000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("🧩", settings.ExtensionGlyphs[".cs"]);
    }

    [Fact]
    public async Task SavingUpdatesShowShellIcons()
    {
        var settings = new AppSettings { ShowShellIcons = true };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, ShowShellIcons: false, EverythingMaxResults: 1000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.False(settings.ShowShellIcons);
    }

    [Fact]
    public async Task SavingUpdatesEverythingMaxResults()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, ShowShellIcons: true, EverythingMaxResults: 5000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(5000, settings.EverythingMaxResults);
    }



    [Fact]
    public async Task SavingWritesThroughTheDebouncedSave()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            new Dictionary<string, string> { [".cs"] = "🧩" }, ShowShellIcons: true, EverythingMaxResults: 2000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        f.Store.Flush();

        using var document = JsonDocument.Parse(File.ReadAllText(f.Paths.SettingsFile));
        Assert.Equal(2000, document.RootElement.GetProperty("everythingMaxResults").GetInt32());
    }



    [Fact]
    public async Task WhenSettingsAreNotWritableSavingUpdatesValuesButNotTheFile()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings, settingsWritable: false);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            new Dictionary<string, string> { [".cs"] = "🧩" }, ShowShellIcons: false, EverythingMaxResults: 500);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        f.Store.Flush();

        Assert.Equal(500, settings.EverythingMaxResults);
        Assert.False(File.Exists(f.Paths.SettingsFile));
    }

    [Fact]
    public async Task TurningShellIconsOnKeepsTheExistingPanelsServiceNonNull()
    {
        var settings = new AppSettings { ShowShellIcons = true };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, ShowShellIcons: true, EverythingMaxResults: 1000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.NotNull(f.ViewModel.Panels[0].ShellIconService);
    }

    [Fact]
    public async Task TurningShellIconsOffSetsTheExistingPanelsServiceToNull()
    {
        var settings = new AppSettings { ShowShellIcons = true };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, ShowShellIcons: false, EverythingMaxResults: 1000);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Null(f.ViewModel.Panels[0].ShellIconService);
    }

    [Fact]
    public async Task AnOutOfRampMaxResultsFallsBackToOneThousand()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, ShowShellIcons: true, EverythingMaxResults: 9999);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AppSettings.DefaultEverythingMaxResults, settings.EverythingMaxResults);
    }
}
