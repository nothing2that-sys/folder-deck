using System.Windows.Controls;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using static FolderDeck.App.Tests.StaWpfTestSupport;

namespace FolderDeck.App.Tests;

/// <summary>설정 창 「1회용 창」 탭 — 처음 타일 배치 (2026-10-14 S2).</summary>
public sealed class ScratchSettingsTests
{
    [Fact]
    public async Task TheRequestCarriesTheCurrentScratchLayout()
    {
        var settings = new AppSettings { ScratchLayout = LayoutPreset.Stacked };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(LayoutPreset.Stacked, Assert.Single(f.SettingsEditor.Opened).ScratchLayout);
    }

    [Fact]
    public async Task SavingUpdatesTheScratchLayout()
    {
        var settings = new AppSettings();
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, settings.ShowShellIcons, settings.EverythingMaxResults, LayoutPreset.Quad);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(LayoutPreset.Quad, settings.ScratchLayout);
    }

    /// <summary>새 값을 모르는 결과(세 값만으로 만든 것)는 이미 고른 배치를 기본값으로 되돌리지 않는다.</summary>
    [Fact]
    public async Task AResultWithoutAScratchLayoutLeavesItAlone()
    {
        var settings = new AppSettings { ScratchLayout = LayoutPreset.Single };
        using var f = new MainWindowFixture(settings: settings);
        await f.ViewModel.InitializeAsync();

        f.SettingsEditor.Answer = new SettingsEditResult(
            settings.ExtensionGlyphs, settings.ShowShellIcons, settings.EverythingMaxResults);
        await f.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(LayoutPreset.Single, settings.ScratchLayout);
    }

    [Fact]
    public void TheTabPreselectsTheCurrentLayout()
    {
        var vm = new ScratchSettingsViewModel(LayoutPreset.Quad);

        Assert.Equal(LayoutPreset.Quad, vm.SelectedLayout.Preset);
        Assert.Equal(LayoutPresets.All.Count, vm.Layouts.Count);
    }

    /// <summary>실물 설정 창을 STA 에서 세운다 — 새 탭의 XAML 이 생성 도중 죽지 않는지.</summary>
    [Fact]
    public void TheSettingsDialogConstructsWithTheScratchTab()
    {
        RunOnSta(() =>
        {
            var request = new SettingsEditRequest(
                new Dictionary<string, string>(), true, AppSettings.DefaultEverythingMaxResults,
                new EverythingStatus(EverythingAvailability.DllMissing, null), LayoutPreset.Stacked);

            var dialog = new IconSettingsDialog(request);
            try
            {
                var tab = (StackPanel)dialog.FindName("ScratchTabContent");
                var vm = Assert.IsType<ScratchSettingsViewModel>(tab.DataContext);
                Assert.Equal(LayoutPreset.Stacked, vm.SelectedLayout.Preset);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
