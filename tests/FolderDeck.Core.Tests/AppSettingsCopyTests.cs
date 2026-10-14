using System.Reflection;
using System.Text.Json;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.Core.Tests;

/// <summary>
/// <see cref="AppSettings.CopyFrom"/> (2026-10-14 2차). 상주 프로그램은 설정 인스턴스 하나를 오래 들고 있다가
/// 다른 프로세스의 변경을 이 메서드로 받는다 — <b>속성이 늘었는데 여기서 빠지면 상주가 그 값을 되돌린다</b>.
/// 그래서 손으로 고른 목록이 아니라 리플렉션으로 「공개 쓰기 가능 속성 전부」를 잰다.
/// </summary>
public sealed class AppSettingsCopyTests
{
    /// <summary>모든 값을 기본값과 다르게 채운 설정 한 벌.</summary>
    private static AppSettings NonDefault() => new()
    {
        SchemaVersion = 5,
        SkipLauncher = true,
        LastWorkspaceId = Guid.NewGuid(),
        WorkspaceOrder = [Guid.NewGuid()],
        TileGap = 16,
        ExtensionGlyphs = new Dictionary<string, string> { [".pdf"] = "📕" },
        ShowShellIcons = false,
        EverythingMaxResults = 5000,
        ScratchLayout = LayoutPreset.Quad,
        ScratchHotkey = "Ctrl+Alt+Shift+G",
        UseResidentAgent = false,
        FavoritesCollapsed = true,
    };

    [Fact]
    public void EveryPublicSettablePropertyIsCopied()
    {
        var source = NonDefault();
        var target = new AppSettings();

        target.CopyFrom(source);

        var properties = typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead)
            .ToList();

        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            // 값 비교는 JSON 으로 한다 — 사전·목록도 내용으로 견준다.
            Assert.True(
                JsonSerializer.Serialize(property.GetValue(source), FolderDeckJson.Options)
                    == JsonSerializer.Serialize(property.GetValue(target), FolderDeckJson.Options),
                $"CopyFrom 이 {property.Name} 을(를) 옮기지 않는다");
        }
    }

    /// <summary>
    /// 위 시험이 「기본값과 같아서 우연히 통과」하지 않게 — 시험용 설정이 모든 속성을 기본값과 다르게 채웠는지 잰다.
    /// 새 속성을 더하면 여기서 먼저 깨진다(그 속성을 <see cref="NonDefault"/> 에 채우라는 뜻).
    /// </summary>
    [Fact]
    public void TheFixtureDiffersFromTheDefaultsEverywhere()
    {
        var source = NonDefault();
        var defaults = new AppSettings();

        foreach (var property in typeof(AppSettings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite && p.CanRead))
        {
            Assert.False(
                JsonSerializer.Serialize(property.GetValue(source), FolderDeckJson.Options)
                    == JsonSerializer.Serialize(property.GetValue(defaults), FolderDeckJson.Options),
                $"시험용 설정의 {property.Name} 이(가) 기본값과 같다");
        }
    }

    [Fact]
    public void CollectionsAreCopiedNotShared()
    {
        var source = NonDefault();
        var target = new AppSettings();

        target.CopyFrom(source);
        source.ExtensionGlyphs[".zip"] = "🗜";
        source.WorkspaceOrder!.Add(Guid.NewGuid());

        Assert.False(target.ExtensionGlyphs.ContainsKey(".zip"));
        Assert.Single(target.WorkspaceOrder!);
    }

    [Fact]
    public void AnOldFileReadsWithTheAgentOn()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{ \"schemaVersion\": 5 }", FolderDeckJson.Options)!;

        Assert.True(settings.UseResidentAgent);
    }

    // ── RegisterHotKey 용 변환 ───────────────────────────────────────────────

    [Theory]
    [InlineData("Ctrl+Alt+F", 0x4003u, 0x46u)]
    [InlineData("Ctrl+Alt+Shift+G", 0x4007u, 0x47u)]
    [InlineData("Ctrl+Alt+F11", 0x4003u, 0x7Au)]
    public void RegistrationValuesUseTheRegisterHotKeyLayout(string text, uint modifiers, uint vk)
    {
        Assert.Equal((modifiers, vk), ScratchHotkey.ToRegistration(text));
    }

    [Fact]
    public void NoneRegistersNothing() => Assert.Null(ScratchHotkey.ToRegistration(ScratchHotkey.None));
}
