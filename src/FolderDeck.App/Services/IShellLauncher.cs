using System.Diagnostics;
using System.Runtime.InteropServices;
using FolderDeck.App.Interop;

namespace FolderDeck.App.Services;





public interface IShellLauncher
{

    string? Open(string path);




    string? RevealInExplorer(string path, bool isDirectory);








    string? ShowProperties(string path);
}


public sealed class ShellLauncher : IShellLauncher
{
    public string? Open(string path)
    {
        try
        {

            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }










    public string? RevealInExplorer(string path, bool isDirectory)
    {
        try
        {
            var arguments = isDirectory ? Quote(path) : $"/select,{Quote(path)}";
            using var process = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = arguments,
                UseShellExecute = false,
            });

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }












    public string? ShowProperties(string path)
    {



        using var fpu = FpuGuard.Enter("ShellExecuteExW(properties)");

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "properties",
            lpFile = path,
            nShow = SwShow,
        };

        try
        {
            if (ShellExecuteExW(ref info))
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }


        return new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    private static string Quote(string path) => $"\"{path}\"";


    private const uint SeeMaskInvokeIdList = 0x0000000C;

    private const int SwShow = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;


        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo lpExecInfo);
}
