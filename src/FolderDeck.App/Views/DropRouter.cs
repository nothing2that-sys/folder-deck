using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Operations;
using FolderDeck.Core.Paths;
using IoDirectory = System.IO.Directory;

namespace FolderDeck.App.Views;


public enum DropRoute
{

    None,





    Internal,


    Shell,
}









public static class DropRouter
{













    public static DropRoute Route(IDataObject? data)
    {
        if (data is null)
        {
            return DropRoute.None;
        }

        try
        {
            if (data.GetDataPresent(FolderPanelView.FileDragFormat))
            {
                return DropRoute.Internal;
            }

            return data.GetDataPresent(DataFormats.FileDrop) ? DropRoute.Shell : DropRoute.None;
        }
        catch (COMException)
        {




            return DropRoute.None;
        }
    }











    public static IReadOnlyList<OperationItem> ShellItems(IDataObject data)
    {
        try
        {
            return data.GetData(DataFormats.FileDrop) is string[] paths
                ? [.. paths.Select(p => new OperationItem(p, IoDirectory.Exists(p)))]
                : [];
        }
        catch (COMException)
        {

            return [];
        }
    }











    public static bool ShellDropCopies(DragDropKeyStates keys) =>
        (keys & DragDropKeyStates.ShiftKey) != DragDropKeyStates.ShiftKey;




















    public static bool InternalDropCopies(DragDropKeyStates keys, bool sameVolume)
    {
        if ((keys & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey)
        {
            return true;
        }

        if ((keys & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
        {
            return false;
        }

        return !sameVolume;
    }













    public static bool AllOnSameVolume(IReadOnlyList<OperationItem>? items, string? destination) =>
        !string.IsNullOrWhiteSpace(destination)
        && items is not null
        && items.All(item => FolderPathRules.SameVolume(item.Path, destination));

















    public static (IReadOnlyList<OperationItem> Items, bool Copy)? Resolve(
        IDataObject? data, DragDropKeyStates keys, string? destination)
    {
        switch (Route(data))
        {
            case DropRoute.Internal:



                try
                {
                    return data!.GetData(FolderPanelView.FileDragFormat) is string packed
                        && DragPayloadCodec.Unpack(packed) is { } payload
                        ? (payload.Items,
                            InternalDropCopies(keys, AllOnSameVolume(payload.Items, destination)))
                        : null;
                }
                catch (COMException)
                {

                    return null;
                }

            case DropRoute.Shell:
                return (ShellItems(data!), ShellDropCopies(keys));

            default:
                return null;
        }
    }





    public static DragDropEffects Effect(bool copy) =>
        copy ? DragDropEffects.Copy : DragDropEffects.Move;


    public static DragDropEffects ShellEffect(DragDropKeyStates keys) =>
        Effect(ShellDropCopies(keys));









    public static void TraceDrop(string site, DropRoute route, string? destination, int itemCount) =>
        Trace.WriteLine(
            $"[FolderDeck] drop site={site} route={route} items={itemCount} " +
            $"dest={destination ?? "(없음)"}");
}
