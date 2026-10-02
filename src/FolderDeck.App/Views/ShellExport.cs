using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Views;

public static class ShellExport
{

    public const string PreferredDropEffectFormat = "Preferred DropEffect";

    private const uint DropEffectCopy = 1;

    private const uint DropEffectMove = 2;

    public const DragDropEffects AllowedEffects = DragDropEffects.Copy | DragDropEffects.Move;

    public static void Attach(DataObject data, IReadOnlyList<OperationItem> items, bool move = false)
    {
        data.SetData(DataFormats.FileDrop, items.Select(i => i.Path).ToArray(), autoConvert: true);

        data.SetData(
            PreferredDropEffectFormat,
            new MemoryStream(BitConverter.GetBytes(move ? DropEffectMove : DropEffectCopy)));
    }
}
