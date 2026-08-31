using System.Runtime.InteropServices;
using System.Text;

namespace FolderDeck.App.Interop;

/// <summary>
/// Everything(voidtools) 질의 API — 전역 검색 타일(§4.12)의 native 경계다. 관리 형만
/// 노출한다 — 버퍼·<c>LARGE_INTEGER</c>·<c>FILETIME</c> 마샬링은 <see cref="EverythingNative"/>
/// 안에 가둔다. 그래야 <c>EverythingSearchService</c> 가 가짜로 시험된다(Phase 12 · D69-b).
/// </summary>
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

// 손으로 쓴 [DllImport] 다 — Interop/ 의 관례를 그대로 따랐다(ShellIconNative.cs 주석).
// 문자열은 W 판만 쓴다(§4.12-3). FpuGuard 는 여기서 걸지 않는다 — 호출 묶음을 감싸는
// 자리는 서비스(EverythingSearchService)다.
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

    /// <summary>
    /// 필요한 길이를 먼저 재고 버퍼를 잡는다 — 고정 길이 하나로 끝내지 않는다(§6-1). 반환된
    /// 글자 수가 버퍼 용량-1 에 못 미치면 잘리지 않은 것으로 본다. 그렇지 않으면 버퍼를
    /// 두 배로 늘려 다시 부른다.
    /// </summary>
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
