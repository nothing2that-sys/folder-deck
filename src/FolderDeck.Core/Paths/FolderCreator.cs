using IoPath = System.IO.Path;

namespace FolderDeck.Core.Paths;















public static class FolderCreator
{








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
