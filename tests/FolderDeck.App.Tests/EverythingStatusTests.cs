using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;






public sealed class EverythingStatusTests
{
    [Fact]
    public void DllMissingHasItsOwnMessage()
    {
        var status = new EverythingStatus(EverythingAvailability.DllMissing, null);

        Assert.Equal(
            "Everything64.dll 을 찾을 수 없다 — 설치가 온전한지 확인하세요.", status.Describe());
    }

    [Fact]
    public void NotRunningHasItsOwnMessage()
    {
        var status = new EverythingStatus(EverythingAvailability.NotRunning, null);

        Assert.Equal("Everything 이 실행 중이 아니다 — Everything 을 켜세요.", status.Describe());
    }

    [Fact]
    public void RunningIncludesTheVersion()
    {
        var status = new EverythingStatus(EverythingAvailability.Running, "1.4.1.1024");

        Assert.Equal("Everything 1.4.1.1024 실행 중", status.Describe());
    }


    [Fact]
    public void TheThreeAvailabilitiesProduceDistinctMessages()
    {
        var missing = new EverythingStatus(EverythingAvailability.DllMissing, null).Describe();
        var notRunning = new EverythingStatus(EverythingAvailability.NotRunning, null).Describe();
        var running = new EverythingStatus(EverythingAvailability.Running, "1.0.0.0").Describe();

        Assert.Equal(3, new[] { missing, notRunning, running }.Distinct().Count());
    }



    [Fact]
    public void VersionTooOldMessageIncludesTheReadVersion()
    {
        var status = new EverythingStatus(EverythingAvailability.VersionTooOld, "1.4.0.1000");

        Assert.Contains("1.4.0.1000", status.Describe());
    }

    [Fact]
    public void IndexNotLoadedHasADifferentMessageThanNotRunning()
    {
        var indexNotLoaded = new EverythingStatus(EverythingAvailability.IndexNotLoaded, null).Describe();
        var notRunning = new EverythingStatus(EverythingAvailability.NotRunning, null).Describe();

        Assert.NotEqual(notRunning, indexNotLoaded);
    }


    [Fact]
    public void TheFiveAvailabilitiesProduceDistinctNonEmptyMessages()
    {
        var messages = new[]
        {
            new EverythingStatus(EverythingAvailability.DllMissing, null).Describe(),
            new EverythingStatus(EverythingAvailability.VersionTooOld, "1.4.0.1000").Describe(),
            new EverythingStatus(EverythingAvailability.NotRunning, null).Describe(),
            new EverythingStatus(EverythingAvailability.IndexNotLoaded, null).Describe(),
            new EverythingStatus(EverythingAvailability.Running, "1.4.1.1024").Describe(),
        };

        Assert.Equal(5, messages.Distinct().Count());
        Assert.All(messages, m => Assert.False(string.IsNullOrEmpty(m)));
    }
}
