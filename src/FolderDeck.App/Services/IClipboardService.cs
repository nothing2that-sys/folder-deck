using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Services;




public interface IClipboardService
{

    string? SetText(string text);












    string? SetFiles(IReadOnlyList<OperationItem> items, bool move = false);








    System.Windows.IDataObject? GetData();
}


public sealed class ClipboardService : IClipboardService
{
    public string? SetText(string text)
    {
        try
        {


            System.Windows.Clipboard.SetText(text);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }













    public static System.Windows.DataObject FileDataObject(
        IReadOnlyList<OperationItem> items, bool move = false)
    {
        var data = new System.Windows.DataObject();
        ShellExport.Attach(data, items, move);
        return data;
    }


    public string? SetFiles(IReadOnlyList<OperationItem> items, bool move = false)
    {
        try
        {

            using var fpu = FolderDeck.App.Interop.FpuGuard.Enter("Clipboard.SetDataObject");



            System.Windows.Clipboard.SetDataObject(FileDataObject(items, move), copy: true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }








    public System.Windows.IDataObject? GetData()
    {
        try
        {

            using var fpu = FolderDeck.App.Interop.FpuGuard.Enter("Clipboard.GetDataObject");

            return System.Windows.Clipboard.GetDataObject();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
