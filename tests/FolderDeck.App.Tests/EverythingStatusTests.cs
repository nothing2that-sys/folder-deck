using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// 감지 결과를 안내 문구로 바꾸는 순수 함수만 문다(D65 §5). 실제 DLL 호출(<see
/// cref="EverythingProbe.Detect"/>)은 시험 기계에 Everything 이 돌고 있는지에 따라 결과가
/// 달라지므로 여기서 부르지 않는다 — 배선은 손 확인으로만 잡힌다(D52·D55·D58 와 같은 자리).
/// </summary>
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

    /// <summary>세 갈래가 서로 다른 문구인지 — 하나로 뭉개지지 않았는지 확인한다.</summary>
    [Fact]
    public void TheThreeAvailabilitiesProduceDistinctMessages()
    {
        var missing = new EverythingStatus(EverythingAvailability.DllMissing, null).Describe();
        var notRunning = new EverythingStatus(EverythingAvailability.NotRunning, null).Describe();
        var running = new EverythingStatus(EverythingAvailability.Running, "1.0.0.0").Describe();

        Assert.Equal(3, new[] { missing, notRunning, running }.Distinct().Count());
    }

    // ── D69-d — 판이 낡음 · 색인 미적재 ──────────────────────────────────────

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

    /// <summary>다섯 갈래가 서로 다른 문구인지 — 하나로 뭉개지지 않았는지 확인한다(D69-d).</summary>
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
