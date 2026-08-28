using System.Text.Json;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 전역 설정의 schemaVersion 1 → 2 (<c>tileGap</c>) — <b>구파일을 거부하지 않고, 조용히
/// 화면을 바꾸지도 않는다</b> (§7.2 · D56).
/// </summary>
/// <remarks>
/// <see cref="SchemaMigrationTests"/> 는 <see cref="Workspace"/> 만 다룬다 — <c>settings.json</c>
/// 의 마이그레이션을 무는 판은 이 파일이 처음이다.
/// <para>
/// 이 회차가 가장 무서워한 것은 「<b>옛 설정의 화면이 조용히 바뀌는 것</b>」이다. 슬롯이 없는
/// 파일도, 사람이 손으로 아무 숫자나 적어 넣은 파일도 <b>8</b>(이 회차 전의 간격)로 읽혀야 한다.
/// </para>
/// </remarks>
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
            // 정리 실패는 무시.
        }
    }

    /// <summary>손으로 적어 넣은 것과 같은 모양의 파일을 그대로 쓴다 — 직렬화기를 거치지 않는다.</summary>
    private void WriteSettingsFile(string json) => File.WriteAllText(_paths.SettingsFile, json);

    // ── 스키마 ───────────────────────────────────────────────────────────────

    /// <summary>D59 가 <c>extensionGlyphs</c> 를 더하며 3 으로 올렸다 — 이 판의 이름도 그만큼 바뀐다.</summary>
    [Fact]
    public void CurrentSchemaVersionIsThree() =>
        Assert.Equal(3, AppSettings.CurrentSchemaVersion);

    /// <summary>워크스페이스 스키마는 이 회차와 무관하다 — 간격은 앱 전체의 값이다(결정 4).</summary>
    [Fact]
    public void TheWorkspaceSchemaDidNotMove() =>
        Assert.Equal(6, Workspace.CurrentSchemaVersion);

    // ── 왕복 ─────────────────────────────────────────────────────────────────

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

    /// <summary>다섯 값이 전부 파일을 왕복한다 — 하나만 되는 것이 아니다.</summary>
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

    /// <summary>고를 수 있는 값은 램프 다섯뿐이다 — 임의의 숫자를 받지 않는다(결정 2).</summary>
    [Fact]
    public void TheChoicesAreTheFiveRampValues() =>
        Assert.Equal([4, 6, 8, 12, 16], AppSettings.TileGapChoices);

    // ── 마이그레이션 (결정 5) — 이 회차에서 가장 중요한 자리 ──────────────────

    /// <summary>
    /// ① <b>간격 슬롯이 아예 없는</b> v1 파일 → <b>8</b>. 옛 설정의 화면이 조용히 바뀌면 안 된다.
    /// </summary>
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

    /// <summary>
    /// ② <b>램프 밖 값</b>이 든 파일 → <b>8</b>. 사람이 손으로 고칠 수 있는 파일이라(§7.2)
    /// 아무 숫자나 들어올 수 있다.
    /// </summary>
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

    /// <summary>램프 밖 값은 <b>메모리에서도</b> 떨어진다 — 직렬화기를 지나야만 걸리는 것이 아니다.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-4)]
    public void SettingATileGapOutsideTheRampFallsBackToTheOldSpacing(int gap) =>
        Assert.Equal(8, new AppSettings { TileGap = gap }.TileGap);

    /// <summary>새 설정 한 벌의 기본값도 8 이다 — 첫 실행이 지금 화면과 같아야 한다.</summary>
    [Fact]
    public void AFreshSettingsObjectStartsAtTheOldSpacing() =>
        Assert.Equal(8, new AppSettings().TileGap);

    /// <summary>v1 파일을 저장하면 지금 판(D59 기준 3)으로 승격된다 — 슬롯도 파일에 적힌다.</summary>
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

        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(12, document.RootElement.GetProperty("tileGap").GetInt32());
    }

    /// <summary>
    /// 모르는 앞선 판본은 여전히 거절한다 — 승격이 그 방어를 지우지 않았다. D59 가 3 까지
    /// 정의했으므로 "아직 모르는 판본"은 4 로 옮겼다(이 판이 걸었던 리터럴 3 은 D59 전에는
    /// 미래 판이었지만 지금은 현재 판이라 그대로 두면 거짓 양성이 아니라 거짓 음성 —
    /// <c>Failure</c> 가 <c>null</c> 이 되어 조용히 통과해버린다).
    /// </summary>
    [Fact]
    public void AFileFromANewerVersionIsStillRejected()
    {
        WriteSettingsFile("""
            {
              "schemaVersion": 4
            }
            """);

        Assert.Equal(StorageFailureKind.UnsupportedSchemaVersion, _store.LoadSettings().Failure!.Kind);
    }
}
