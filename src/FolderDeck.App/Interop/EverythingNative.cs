using System.Runtime.InteropServices;
using System.Text;

namespace FolderDeck.App.Interop;

public interface IEverythingNative
{
    void SetSearch(string query);
    void SetMax(uint max);
    void SetSort(uint sort);
    void SetRequestFlags(uint flags);
    bool Query(bool wait);
    uint GetNumResults();
    uint GetTotResults();
    uint GetLastError();
    bool IsDbLoaded();
    string? GetResultFullPath(uint index);
    long? GetResultSize(uint index);
    DateTime? GetResultDateModifiedUtc(uint index);
    uint GetResultAttributes(uint index);
    void Reset();
}

public sealed class EverythingNative : IEverythingNative
{
    private const int InitialPathBufferChars = 260;
    private const int MaxPathBufferChars = 65536;

    public void SetSearch(string query) => Everything_SetSearchW(query);

    public void SetMax(uint max) => Everything_SetMax(max);

    public void SetSort(uint sort) => Everything_SetSort(sort);

    public void SetRequestFlags(uint flags) => Everything_SetRequestFlags(flags);

    public bool Query(bool wait) => Everything_QueryW(wait);

    public uint GetNumResults() => Everything_GetNumResults();

    public uint GetTotResults() => Everything_GetTotResults();

    public uint GetLastError() => Everything_GetLastError();

    public bool IsDbLoaded() => Everything_IsDBLoaded();

    public void Reset() => Everything_Reset();

    public string? GetResultFullPath(uint index)
    {
        var capacity = InitialPathBufferChars;

        while (true)
        {
            var buffer = new StringBuilder(capacity);
            var written = Everything_GetResultFullPathNameW(index, buffer, (uint)capacity);

            if (written == 0)
            {
                return null;
            }

            if (written < capacity - 1 || capacity >= MaxPathBufferChars)
            {
                return buffer.ToString(0, (int)written);
            }

            capacity *= 2;
        }
    }

    public long? GetResultSize(uint index) =>
        Everything_GetResultSize(index, out var size) ? size : null;

    public DateTime? GetResultDateModifiedUtc(uint index) =>
        Everything_GetResultDateModified(index, out var fileTime)
            ? DateTime.FromFileTimeUtc(FileTimeToLong(fileTime))
            : null;

    public uint GetResultAttributes(uint index) => Everything_GetResultAttributes(index);

    private static long FileTimeToLong(FILETIME fileTime) =>
        ((long)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_SetSearchW(string lpString);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetMax(uint dwMax);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetSort(uint dwSort);

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetTotResults();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetLastError();

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_IsDBLoaded();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern uint Everything_GetResultFullPathNameW(
        uint dwIndex, StringBuilder wbuf, uint wbufSizeInWChars);

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultSize(uint dwIndex, out long lpSize);

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultDateModified(uint dwIndex, out FILETIME lpDateModified);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetResultAttributes(uint dwIndex);

    [DllImport("Everything64.dll")]
    private static extern void Everything_Reset();
}
