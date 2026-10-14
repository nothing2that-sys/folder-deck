namespace FolderDeck.App.Services;

/// <summary>
/// <c>--scratch</c>(시작 메뉴 바로가기)를 상주에게 맡길지 (2026-10-14 2차 A4). 상주 안에서 여는 쪽이 빠르다.
/// </summary>
public static class ScratchLaunch
{
    /// <summary>
    /// 상주를 쓰는 설정이고 상주가 살아 있어 신호가 닿았으면 true — 부르는 프로세스는 아무것도 안 띄우고 끝난다.
    /// 상주를 안 쓰거나, 없거나, 신호가 실패하면 false — 부르는 프로세스가 스스로 연다.
    /// </summary>
    public static bool DelegatedToAgent(bool useResidentAgent, IAgentControl agent) =>
        useResidentAgent && agent.IsRunning && agent.TrySignalScratch();
}
