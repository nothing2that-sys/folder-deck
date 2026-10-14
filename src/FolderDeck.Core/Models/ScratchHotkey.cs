namespace FolderDeck.Core.Models;

/// <summary>
/// 1회용 창 단축키의 글자 표기 ↔ 바로가기(.lnk) 단축키 값 (2026-10-14 S4). 순수 함수만 둔다 —
/// 실제 .lnk 쓰기는 App 의 <c>ScratchShortcut</c> 이 한다.
/// </summary>
/// <remarks>
/// .lnk 바로 가기 키가 받는 조합만 허용한다: <b><c>Ctrl+Alt</c>(선택적으로 <c>+Shift</c>) + 영문자 ·
/// 숫자 · F1~F12</b>. 탐색기는 .lnk 단축키로 Ctrl+Alt 가 없는 조합을 쓰지 않는다(단일 글자를 넣으면
/// 스스로 Ctrl+Alt 를 붙인다) — 그 밖의 조합을 받아 주면 저장은 되는데 눌러도 안 뜨는 상태가 된다.
/// <para>
/// 표기는 <c>Ctrl+Alt+F</c> · <c>Ctrl+Alt+Shift+F5</c> 꼴로 하나만 쓴다. 빈 문자열은 「없음」이다.
/// </para>
/// </remarks>
public static class ScratchHotkey
{
    /// <summary>처음 설치했을 때의 기본값. 설치 프로그램(<c>FolderDeck.iss</c>)의 <c>HotKey</c> 와 같다.</summary>
    public const string Default = "Ctrl+Alt+F";

    /// <summary>「없음」.</summary>
    public const string None = "";

    private const byte HotkeyfShift = 0x01;
    private const byte HotkeyfControl = 0x02;
    private const byte HotkeyfAlt = 0x04;

    /// <summary>
    /// 수정키와 키 이름에서 표기를 만든다. 허용 안 되는 조합이면 null 과 이유를 낸다.
    /// </summary>
    /// <param name="keyName">「A」~「Z」 · 「0」~「9」 · 「F1」~「F12」.</param>
    public static string? Compose(bool ctrl, bool alt, bool shift, string? keyName, out string? reason)
    {
        if (!ctrl || !alt)
        {
            reason = "Ctrl 과 Alt 를 함께 눌러야 한다 — 바로가기 단축키는 Ctrl+Alt 조합만 받는다.";
            return null;
        }

        if (VirtualKey(keyName) is null)
        {
            reason = "영문자 · 숫자 · F1~F12 중 하나여야 한다.";
            return null;
        }

        reason = null;
        return Format(shift, keyName!.ToUpperInvariant());
    }

    /// <summary>표기를 정규형으로 다듬는다. 「없음」(빈 문자열·null)은 <see cref="None"/>. 못 읽으면 false.</summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        var key = parts[^1];
        var modifiers = parts[..^1].Select(p => p.ToUpperInvariant()).ToList();
        var ctrl = modifiers.Remove("CTRL");
        var alt = modifiers.Remove("ALT");
        var shift = modifiers.Remove("SHIFT");
        if (modifiers.Count > 0)
        {
            return false;
        }

        if (Compose(ctrl, alt, shift, key, out _) is not { } composed)
        {
            return false;
        }

        normalized = composed;
        return true;
    }

    /// <summary>
    /// .lnk 의 단축키 값(<c>IShellLink::SetHotkey</c> 의 WORD — 낮은 바이트 가상 키, 높은 바이트
    /// <c>HOTKEYF_*</c>). 「없음」은 0. 정규형이 아니면 던진다 — 호출부가 먼저 <see cref="TryNormalize"/>.
    /// </summary>
    public static ushort ToShortcutValue(string normalized)
    {
        if (normalized == None)
        {
            return 0;
        }

        if (!TryNormalize(normalized, out var checkedText) || checkedText != normalized)
        {
            throw new ArgumentException($"정규형 단축키가 아니다: {normalized}", nameof(normalized));
        }

        var shift = normalized.Contains("+Shift+", StringComparison.Ordinal);
        var vk = VirtualKey(normalized[(normalized.LastIndexOf('+') + 1)..])!.Value;
        var modifiers = (byte)(HotkeyfControl | HotkeyfAlt | (shift ? HotkeyfShift : 0));
        return (ushort)((modifiers << 8) | vk);
    }

    /// <summary>.lnk 단축키 값 → 표기. 0 은 「없음」. 이 규칙으로 쓸 수 없는 값이면 null.</summary>
    public static string? FromShortcutValue(ushort value)
    {
        if (value == 0)
        {
            return None;
        }

        var vk = (byte)(value & 0xFF);
        var modifiers = (byte)(value >> 8);
        var ctrl = (modifiers & HotkeyfControl) != 0;
        var alt = (modifiers & HotkeyfAlt) != 0;
        var shift = (modifiers & HotkeyfShift) != 0;

        return KeyName(vk) is { } key && (modifiers & ~(HotkeyfControl | HotkeyfAlt | HotkeyfShift)) == 0
            ? Compose(ctrl, alt, shift, key, out _)
            : null;
    }

    // ── RegisterHotKey 용 (2026-10-14 2차 · 상주 프로그램) ──────────────────────

    /// <summary><c>RegisterHotKey</c> 의 수식키 비트. .lnk 의 <c>HOTKEYF_*</c> 와 <b>배치가 다르다</b>(ALT 1 · CONTROL 2 · SHIFT 4).</summary>
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>누르고 있는 동안 되풀이 알림을 받지 않는다 — 창이 줄줄이 뜨지 않게.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// 정규형 표기 → <c>RegisterHotKey(hwnd, id, modifiers, vk)</c> 의 (modifiers, vk). 「없음」이면 null
    /// (등록하지 않는다). 정규형이 아니면 던진다 — <see cref="ToShortcutValue"/> 와 같은 계약이다.
    /// </summary>
    public static (uint Modifiers, uint VirtualKey)? ToRegistration(string normalized)
    {
        var value = ToShortcutValue(normalized);
        if (value == 0)
        {
            return null;
        }

        var shift = (value >> 8 & HotkeyfShift) != 0;
        var modifiers = ModControl | ModAlt | ModNoRepeat | (shift ? ModShift : 0);
        return (modifiers, (uint)(value & 0xFF));
    }

    private static string Format(bool shift, string key) => shift ? $"Ctrl+Alt+Shift+{key}" : $"Ctrl+Alt+{key}";

    private static byte? VirtualKey(string? keyName)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            return null;
        }

        var key = keyName.ToUpperInvariant();
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return (byte)key[0];
        }

        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 12
            && key[1..] == n.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return (byte)(0x70 + n - 1);
        }

        return null;
    }

    private static string? KeyName(byte vk) => vk switch
    {
        >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x70 + 1}",
        _ => null,
    };
}
