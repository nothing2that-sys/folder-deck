using System.Diagnostics;

namespace FolderDeck.App.Services;

/// <summary>
/// 셸에 넘기는 동작 (§4.5 · §4.6). COM 셸 컨텍스트 메뉴가 아니라 단순 실행 수준이다.
/// 테스트에서 갈아끼우기 위해 인터페이스로 둔다.
/// </summary>
public interface IShellLauncher
{
    /// <summary>OS 기본 프로그램으로 열기. 성공하면 null, 실패하면 사용자에게 보일 메시지.</summary>
    string? Open(string path);

    /// <summary>
    /// 탐색기에서 열기 — 폴더면 그 폴더를, 파일이면 그 파일이 든 폴더를 열고 파일을 선택한다.
    /// </summary>
    string? RevealInExplorer(string path, bool isDirectory);
}

/// <inheritdoc/>
public sealed class ShellLauncher : IShellLauncher
{
    public string? Open(string path)
    {
        try
        {
            // UseShellExecute = true 라야 확장자 연결이 적용된다(실행이 아니라 "열기").
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <remarks>
    /// 호출 형태는 실측으로 정했다(§11 — 기억으로 단정하지 말 것):
    /// <list type="bullet">
    /// <item><c>ArgumentList</c> 는 <c>/select,</c> 를 깨뜨린다 — 인자별로 다시 인용되면서
    /// explorer 가 경로를 못 읽고 "홈"을 열었다. 그래서 <c>Arguments</c> 단일 문자열에
    /// 직접 인용해서 넘긴다.</item>
    /// <item>explorer.exe 는 <b>성공해도 ExitCode 1</b> 을 준다. 종료 코드로 실패를 판정하면 안 된다.</item>
    /// </list>
    /// </remarks>
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

    private static string Quote(string path) => $"\"{path}\"";
}
