using FolderDeck.App.Interop;

namespace FolderDeck.App.Tests;

/// <summary>
/// <see cref="PreviewHandlerHost"/> 의 서로게이트 활성화 시도(2026-09-18 프로브
/// <c>doc/Probe_2026-09-18_risk3_report.md</c> §2.3 — "시도 후 폴백"). 실제 등록된
/// 서로게이트 핸들러(.pdf류)로 성공 경로를 확정 짓는 시험은 환경(설치된 앱)에 따라
/// 갈리므로 여기 담지 않는다 — 대신 "등록이 없으면 조용히 실패해 폴백을 부른다"는,
/// 어느 환경에서나 참인 안전 계약만 시험한다.
/// </summary>
public sealed class PreviewHandlerHostTests
{
    [Fact]
    public void ActivatingAnUnregisteredClsidFailsGracefullyWithoutThrowing()
    {
        var result = PreviewHandlerHost.TryActivateOutOfProcess(Guid.NewGuid());

        Assert.Null(result);
    }
}
