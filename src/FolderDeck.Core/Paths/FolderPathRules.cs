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
}
