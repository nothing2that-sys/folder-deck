using System.Runtime.InteropServices;

namespace FolderDeck.App.Services;





public enum EverythingAvailability
{
    DllMissing,
    NotRunning,
    Running,
    VersionTooOld,
    IndexNotLoaded,
}






public sealed record EverythingStatus(EverythingAvailability Availability, string? Version)
{

    public string Describe() => Availability switch
    {
        EverythingAvailability.DllMissing =>
            "Everything64.dll 을 찾을 수 없다 — 설치가 온전한지 확인하세요.",
        EverythingAvailability.VersionTooOld =>
            $"Everything {Version} 은 너무 낡았다 — Everything 을 새 판으로 올리세요.",
        EverythingAvailability.NotRunning =>
            "Everything 이 실행 중이 아니다 — Everything 을 켜세요.",
        EverythingAvailability.IndexNotLoaded =>
            "Everything 이 색인을 읽는 중이다 — 잠시 뒤 다시 해 보세요.",
        EverythingAvailability.Running => $"Everything {Version} 실행 중",
        _ => "알 수 없는 상태다.",
    };
}






public static class EverythingProbe
{

    private const uint EverythingErrorIpc = 2;






    private static readonly Version MinimumVersion = new(1, 4, 1);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetMajorVersion();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetMinorVersion();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetRevision();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetBuildNumber();

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetLastError();

    [DllImport("Everything64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_IsDBLoaded();













    public static EverythingStatus Detect()
    {
        try
        {
            var major = Everything_GetMajorVersion();
            var error = Everything_GetLastError();

            if (error == EverythingErrorIpc)
            {
                return new EverythingStatus(EverythingAvailability.NotRunning, null);
            }

            var minor = Everything_GetMinorVersion();
            var revision = Everything_GetRevision();
            var build = Everything_GetBuildNumber();
            var version = $"{major}.{minor}.{revision}.{build}";
            var dbLoaded = Everything_IsDBLoaded();

            var availability = Decide(major, minor, revision, error, dbLoaded);

            return new EverythingStatus(
                availability,
                availability is EverythingAvailability.Running or EverythingAvailability.VersionTooOld
                    ? version
                    : null);
        }
        catch (DllNotFoundException)
        {
            return new EverythingStatus(EverythingAvailability.DllMissing, null);
        }
        catch (EntryPointNotFoundException)
        {
            return new EverythingStatus(EverythingAvailability.DllMissing, null);
        }
        catch (BadImageFormatException)
        {


            return new EverythingStatus(EverythingAvailability.DllMissing, null);
        }
    }







    internal static EverythingAvailability Decide(
        uint major, uint minor, uint revision, uint lastError, bool dbLoaded)
    {
        if (lastError == EverythingErrorIpc)
        {
            return EverythingAvailability.NotRunning;
        }

        if (new Version((int)major, (int)minor, (int)revision) < MinimumVersion)
        {
            return EverythingAvailability.VersionTooOld;
        }

        if (!dbLoaded)
        {
            return EverythingAvailability.IndexNotLoaded;
        }

        return EverythingAvailability.Running;
    }
}
