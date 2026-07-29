using System.Diagnostics;

namespace FolderDeck.App.Services;

/// <summary>
/// 자기 자신(FolderDeck.exe)을 새 프로세스로 띄운다 — 워크스페이스당 프로세스 구조의 유일한
/// 진입 수단이다. 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface ISelfLauncher
{
    /// <summary>그 워크스페이스를 새 창(새 프로세스)으로 연다. 성공하면 null, 실패하면 메시지.</summary>
    string? OpenWorkspace(Guid workspaceId);

    /// <summary>런처를 띄운다. 이미 떠 있으면 그 프로세스가 기존 런처를 앞으로 보낸다 (§4.3).</summary>
    string? OpenLauncher();
}

/// <inheritdoc/>
/// <remarks>
/// <c>Environment.ProcessPath</c> 는 관리 dll 이 아니라 <b>실행 중인 exe</b> 를 준다
/// (개발 중 <c>dotnet run</c> 으로 띄운 경우에도 apphost exe — 프로브로 확인했다).
/// 그래서 배포 형태에 상관없이 같은 방식으로 자기 자신을 다시 띄울 수 있다.
/// </remarks>
public sealed class SelfLauncher : ISelfLauncher
{
    /// <inheritdoc/>
    public string? OpenWorkspace(Guid workspaceId) =>
        Start(CommandLineOptions.WorkspaceSwitch, workspaceId.ToString());

    /// <inheritdoc/>
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
            // ArgumentList 로 넘긴다 — 경로에 공백이 있어도 인용이 알아서 맞는다.
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
