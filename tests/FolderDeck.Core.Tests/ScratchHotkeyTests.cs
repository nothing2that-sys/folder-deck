using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

/// <summary>1회용 창 단축키 표기 ↔ .lnk 단축키 값 (2026-10-14 S4).</summary>
public sealed class ScratchHotkeyTests
{
    [Theory]
    [InlineData(false, "F", "Ctrl+Alt+F")]
    [InlineData(true, "f", "Ctrl+Alt+Shift+F")]
    [InlineData(false, "7", "Ctrl+Alt+7")]
    [InlineData(false, "F1", "Ctrl+Alt+F1")]
    [InlineData(true, "F12", "Ctrl+Alt+Shift+F12")]
    public void AllowedCombinationsCompose(bool shift, string key, string expected)
    {
        Assert.Equal(expected, ScratchHotkey.Compose(ctrl: true, alt: true, shift, key, out var reason));
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(true, false, "F")]   // Ctrl+F
    [InlineData(false, true, "Space")] // Alt+Space
    [InlineData(true, true, "F13")]
    [InlineData(true, true, "Space")]
    [InlineData(true, true, "F0")]
    [InlineData(true, true, "F01")]
    [InlineData(true, true, "")]
    public void DisallowedCombinationsAreRefusedWithAReason(bool ctrl, bool alt, string key)
    {
        Assert.Null(ScratchHotkey.Compose(ctrl, alt, shift: false, key, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData("ctrl+alt+f", "Ctrl+Alt+F")]
    [InlineData("Alt + Ctrl + Shift + g", "Ctrl+Alt+Shift+G")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TextNormalizes(string? text, string expected)
    {
        Assert.True(ScratchHotkey.TryNormalize(text, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("Ctrl+F")]
    [InlineData("Ctrl+Alt+Win+F")]
    [InlineData("Ctrl+Alt+Ctrl+F")]
    [InlineData("F")]
    public void UnreadableTextIsRejected(string text) =>
        Assert.False(ScratchHotkey.TryNormalize(text, out _));

    /// <summary>
    /// WORD 배치: 낮은 바이트 가상 키, 높은 바이트 HOTKEYF_*(SHIFT 1 · CONTROL 2 · ALT 4).
    /// <c>Ctrl+Alt+F</c> = (2|4)&lt;&lt;8 | 'F'(0x46) = 0x0646.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+Alt+F", 0x0646)]
    [InlineData("Ctrl+Alt+Shift+F", 0x0746)]
    [InlineData("Ctrl+Alt+F5", 0x0674)]
    [InlineData("Ctrl+Alt+0", 0x0630)]
    [InlineData("", 0)]
    public void ShortcutValuesMatchTheShellLayout(string text, int expected)
    {
        Assert.Equal((ushort)expected, ScratchHotkey.ToShortcutValue(text));
        Assert.Equal(text, ScratchHotkey.FromShortcutValue((ushort)expected));
    }

    /// <summary>이 규칙으로 쓸 수 없는 .lnk 값(Ctrl 없이 Alt+F, 확장 비트)은 표기로 못 바꾼다.</summary>
    [Theory]
    [InlineData(0x0446)]
    [InlineData(0x0E46)]
    [InlineData(0x0620)]
    public void ForeignShortcutValuesHaveNoText(int value) =>
        Assert.Null(ScratchHotkey.FromShortcutValue((ushort)value));

    [Fact]
    public void SettingsDefaultToTheInstallerKeyAndRejectGarbage()
    {
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Alt+F", settings.ScratchHotkey);

        settings.ScratchHotkey = "ctrl+alt+shift+g";
        Assert.Equal("Ctrl+Alt+Shift+G", settings.ScratchHotkey);

        settings.ScratchHotkey = "";
        Assert.Equal(ScratchHotkey.None, settings.ScratchHotkey);

        settings.ScratchHotkey = "Ctrl+F";
        Assert.Equal(ScratchHotkey.Default, settings.ScratchHotkey);
    }
}
