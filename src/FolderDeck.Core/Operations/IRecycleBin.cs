namespace FolderDeck.Core.Operations;

/// <summary>
/// 휴지통으로 보내는 통로 (§4.4 — 영구 삭제가 아니라 되돌릴 수 있게).
/// </summary>
/// <remarks>
/// 실제 구현은 Windows 셸 호출이라 플랫폼 중립인 Core 밖(App)에 둔다. 엔진은 이 계약만 안다 —
/// 테스트는 가짜를 끼워 실제 휴지통을 건드리지 않는다.
/// <para>
/// 예외를 던지지 않고 <b>사유를 돌려준다</b>: 파일 하나가 잠겨 있다고 나머지 삭제가
/// 통째로 멈추면 안 되기 때문이다(§5 부분 실패).
/// </para>
/// </remarks>
public interface IRecycleBin
{
    /// <summary>휴지통으로 보낸다. 성공하면 null, 실패하면 사람이 읽을 수 있는 사유.</summary>
    string? Send(string path);
}
