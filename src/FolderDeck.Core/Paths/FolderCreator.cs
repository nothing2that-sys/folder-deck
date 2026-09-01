using IoPath = System.IO.Path;

namespace FolderDeck.Core.Paths;

/// <summary>
/// 폴더 하나를 새로 만든다 (§4.6 · D75). 이름 규칙은 <see cref="FolderPathRules.RejectLeafName"/>
/// 을 그대로 태운다 — 이름 바꾸기와 규칙이 갈라지면 안 된다.
/// </summary>
/// <remarks>
/// <see cref="FolderPathRules"/> 와 달리 <b>순수하지 않다</b>(성공하면 실제로
/// <see cref="Directory.CreateDirectory(string)"/> 를 부른다) — 그래서 같은 파일에 묶지 않고
/// 옆에 둔다.
/// <para>
/// 실패 표현은 이 저장소가 이미 쓰는 관례를 그대로 따른다 — <b>거절 이유 문자열, 성공하면
/// <c>null</c></b>(<see cref="FolderPathRules.RejectLeafName"/> · <c>IRecycleBin.Send</c> 와
/// 같은 모양이다, D75-b B-0 §D.3). 새 관례(예외·전용 Result 클래스)를 만들지 않는다.
/// </para>
/// </remarks>
public static class FolderCreator
{
    /// <summary>
    /// <paramref name="parentPath"/> 아래 <paramref name="name"/> 이름의 폴더를 만든다.
    /// 거절 이유가 있으면 그것을, 성공하면 <c>null</c> 을 돌려준다.
    /// </summary>
    /// <remarks>
    /// <b>거절할 때는 파일 시스템을 건드리지 않는다</b> — 이름 검증·부모 확인·중복 확인이
    /// 전부 <c>Directory.CreateDirectory</c> 앞에 있다.
    /// </remarks>
    public static string? Create(string parentPath, string? name)
    {
        if (FolderPathRules.RejectLeafName(name) is { } rejected)
        {
            return rejected;
        }

        if (!Directory.Exists(parentPath))
        {
            return $"부모 폴더가 없다: {parentPath}";
        }

        var target = IoPath.Combine(parentPath, name!);

        if (Directory.Exists(target) || File.Exists(target))
        {
            return $"이미 있는 이름이다: {name}";
        }

        Directory.CreateDirectory(target);
        return null;
    }
}
