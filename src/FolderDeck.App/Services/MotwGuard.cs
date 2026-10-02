using System.IO;

namespace FolderDeck.App.Services;

internal static class MotwGuard
{

    internal static bool ShouldSkipPreviewHandler(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return true;
        }

        if (!File.Exists(filePath))
        {

            return true;
        }

        return File.Exists(filePath + ":Zone.Identifier");
    }
}
