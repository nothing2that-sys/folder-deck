using System.IO;
using Microsoft.Win32;

namespace FolderDeck.App.Interop;

internal static class PreviewHandlerResolver
{
    private const string PreviewHandlerSubkey = @"shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    internal static Guid? FindHandlerClsid(string filePath, out string source)
    {
        string ext = Path.GetExtension(filePath);
        source = "";
        if (string.IsNullOrEmpty(ext))
            return null;

        string? clsidStr = ReadDefault($@"HKEY_CLASSES_ROOT\{ext}\{PreviewHandlerSubkey}");
        if (clsidStr != null) { source = "확장자 직속"; }

        if (clsidStr == null)
        {
            string? progId = ReadDefault($@"HKEY_CLASSES_ROOT\{ext}");
            if (!string.IsNullOrEmpty(progId))
            {
                clsidStr = ReadDefault($@"HKEY_CLASSES_ROOT\{progId}\{PreviewHandlerSubkey}");
                if (clsidStr != null) { source = $"ProgID({progId})"; }
            }
        }

        if (clsidStr == null)
        {
            clsidStr = ReadDefault($@"HKEY_CLASSES_ROOT\SystemFileAssociations\{ext}\{PreviewHandlerSubkey}");
            if (clsidStr != null) { source = "SystemFileAssociations"; }
        }

        if (clsidStr == null)
            return null;

        return Guid.TryParse(clsidStr, out var g) ? g : (Guid?)null;
    }

    private static string? ReadDefault(string keyPath)
        => Registry.GetValue(keyPath, null, null) as string;
}
