using System.Diagnostics;

namespace FolderDeck.App.Services;

public interface ISelfLauncher
{

    string? OpenWorkspace(Guid workspaceId);

    string? OpenLauncher();
}

public sealed class SelfLauncher : ISelfLauncher
{

    public string? OpenWorkspace(Guid workspaceId) =>
        Start(CommandLineOptions.WorkspaceSwitch, workspaceId.ToString());

    public string? OpenLauncher() => Start(CommandLineOptions.LauncherSwitch);

    private static string? Start(params string[] arguments)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return "실행 파일 경로를 알 수 없다.";
        }

        try
        {

            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
