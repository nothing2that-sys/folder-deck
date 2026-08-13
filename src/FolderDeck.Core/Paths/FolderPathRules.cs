using IoPath = System.IO.Path;

namespace FolderDeck.Core.Paths;

/// <summary>
/// 등록할 폴더 경로를 다듬는 순수 규칙 (§4.11-1). 등록·드롭·런처가 각자 갖게 되면 세 곳의
/// 규칙이 조용히 갈라지므로 <b>Core 한 곳</b>에만 둔다.
/// </summary>
/// <remarks>
/// <see cref="Normalize"/> 와 <see cref="LeafName"/> 은 <c>TrimEnd</c> 까지는 같지만
/// <b>다 깎였을 때의 뒷처리가 다르다</b> — 앞은 원본 경로로 되돌리고, 뒤는 깎인 것을 그대로 쓴다.
/// 하나로 합치면 행위가 바뀐다.
/// </remarks>
public static class FolderPathRules
{
    /// <summary>
    /// Windows 가 장치에 예약해 둔 이름. 확장자를 붙여도(<c>CON.txt</c>) 여전히 못 쓴다.
    /// </summary>
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// 끝의 구분자를 떼어 낸다. 다 깎이면 원본을 그대로 돌려준다 — 루트(<c>C:\</c>)를 빈 문자열로
    /// 만들지 않기 위해서다.
    /// </summary>
    public static string Normalize(string path)
    {
        var normalized = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            normalized = path;
        }

        return normalized;
    }

    /// <summary>경로의 잎 이름. 잎이 없으면(루트 등) 깎인 경로를 이름으로 쓴다.</summary>
    public static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        var name = IoPath.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>
    /// 이름 바꾸기가 받아들일 수 없는 잎 이름이면 <b>그 이유</b>, 받아들일 수 있으면 <c>null</c>
    /// (§4.6 309행 · A군 ②).
    /// </summary>
    /// <remarks>
    /// <b>이름 규칙의 유일한 자리다</b>(D4) — 부르는 쪽에서 금칙 문자를 다시 세거나 예약 이름을
    /// 또 나열하면 두 곳의 규칙이 조용히 갈라진다. <see cref="LeafName"/> 이 경로에서 이름을
    /// 뽑는 쪽이고 이것은 <b>사람이 새로 적은 이름</b>을 보는 쪽이다.
    /// <para>
    /// <b>조용히 다듬지 않고 거절한다.</b> Windows 는 끝의 점과 공백을 말없이 깎으므로
    /// "a." 로 바꾸면 디스크에는 "a" 가 생기고 사람이 적은 것과 화면이 어긋난다. 이유를 말하는
    /// 쪽이 낫다.
    /// </para>
    /// </remarks>
    public static string? RejectLeafName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "빈 이름은 쓸 수 없다";
        }

        if (name is "." or "..")
        {
            return "'.' 과 '..' 는 이름이 될 수 없다";
        }

        // 구분자(\ /)와 : * ? " < > | 그리고 제어문자가 전부 여기 들어 있다 — 목록을 손으로
        // 적지 않는 이유다. 제어문자는 그대로 찍으면 안 보이므로 코드로 보여 준다.
        var at = name.IndexOfAny(IoPath.GetInvalidFileNameChars());
        if (at >= 0)
        {
            var bad = name[at];
            return $"쓸 수 없는 문자가 있다: {(char.IsControl(bad) ? $"\\u{(int)bad:X4}" : bad.ToString())}";
        }

        if (name[^1] is '.' or ' ')
        {
            return "이름 끝에 점이나 공백을 둘 수 없다";
        }

        // 확장자를 붙여도 예약이라 첫 점 앞만 본다.
        var dot = name.IndexOf('.');
        var stem = dot >= 0 ? name[..dot] : name;

        foreach (var reserved in ReservedNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return $"Windows 예약 이름이다: {reserved}";
            }
        }

        return null;
    }
}
