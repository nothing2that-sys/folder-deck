using IoPath = System.IO.Path;

namespace FolderDeck.Core.Paths;










public static class FolderPathRules
{



    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];





    public static string Normalize(string path)
    {
        var normalized = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            normalized = path;
        }

        return normalized;
    }















    public static bool SameVolume(string a, string b) =>
        string.Equals(
            IoPath.GetPathRoot(IoPath.GetFullPath(a)),
            IoPath.GetPathRoot(IoPath.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);


    public static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        var name = IoPath.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }















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
