using System.Collections.Generic;
using System.Threading.Tasks;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.ViewModels;











public sealed record FileDropPayload(string SourceFolder, IReadOnlyList<OperationItem> Items);








public interface IFileDropTarget
{
    FolderEntry Entry { get; }


    bool IsDropTarget { get; set; }






    bool DropTargetCopies { get; set; }
}














public static class DropTargetCaption
{
    public static string For(bool copy) => copy ? "여기로 복사" : "여기로 이동";
}














public interface IShellDropHost
{

    bool CanDropOnto(string? destinationPath, IReadOnlyList<OperationItem>? items);


    Task DropOntoPathAsync(string destinationPath, IReadOnlyList<OperationItem> items, bool copy);
}
