namespace FolderDeck.App.Interop;









public enum ShellMenuAppCommand : uint
{

    None = 0,

    RevealInExplorer = 0x11,
    Rename = 0x12,
    SetAnchor = 0x13,
    RegisterFolder = 0x14,
    Trash = 0x15,


    CompareContent = 0x16,
}


public readonly record struct ShellMenuAppItem(ShellMenuAppCommand Command, string Text);








public static class ShellMenuPolicy
{











    public static bool IsRemovedVerb(string? verb) =>
        string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase);











    public static bool SharesOneParent(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        string? first = System.IO.Path.GetDirectoryName(paths[0]);
        if (string.IsNullOrEmpty(first))
        {
            return false;
        }

        for (int i = 1; i < paths.Count; i++)
        {
            if (!string.Equals(System.IO.Path.GetDirectoryName(paths[i]), first, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }








    public static ShellMenuAppCommand ToAppCommand(uint menuId) =>
        menuId != 0 && menuId < ShellContextMenu.IdCmdFirst && Enum.IsDefined(typeof(ShellMenuAppCommand), menuId)
            ? (ShellMenuAppCommand)menuId
            : ShellMenuAppCommand.None;


























    public static IReadOnlyList<ShellMenuAppItem> AppItems(bool isDirectory, bool canCompareContent = false)
    {
        var items = new List<ShellMenuAppItem>(6)
        {
            new(ShellMenuAppCommand.RevealInExplorer, "탐색기에서 열기"),
            new(ShellMenuAppCommand.Rename, "이름 바꾸기\tF2"),
        };

        if (!isDirectory && canCompareContent)
        {
            items.Add(new(ShellMenuAppCommand.CompareContent, "내용 비교"));
        }

        if (isDirectory)
        {
            items.Add(new(ShellMenuAppCommand.SetAnchor, "이 폴더를 앵커로 지정"));
            items.Add(new(ShellMenuAppCommand.RegisterFolder, "새 폴더로 등록"));
        }

        items.Add(new(ShellMenuAppCommand.Trash, "휴지통으로 삭제\tDel"));
        return items;
    }
}
