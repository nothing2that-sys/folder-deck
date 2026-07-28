using FolderDeck.App.Services;

namespace FolderDeck.App.Tests;

/// <summary>
/// 실제 Windows 휴지통 (§4.4). 여기서 지키려는 것은 <b>P/Invoke 마샬링</b>이다 —
/// 구조체 레이아웃이나 이중 null 종료가 틀리면 조용히 엉뚱한 것을 지우거나 죽는다.
/// </summary>
/// <remarks>
/// 성공 경로(파일이 실제로 휴지통에 들어가 복구 가능한지)는 테스트를 돌릴 때마다 사용자
/// 휴지통에 찌꺼기를 남기므로 자동화하지 않았다. 대신 개발 중 실환경에서 한 번 확인했다:
/// 임시 파일을 <see cref="RecycleBin.Send"/> 로 보내면 반환 0, 디스크에서 사라지고,
/// <c>Shell.Application</c> 네임스페이스 10(휴지통)에서 원래 경로와 함께 조회된다.
/// </remarks>
public sealed class RecycleBinTests
{
    private readonly RecycleBin _bin = new();

    /// <summary>
    /// 없는 경로 = 셸이 코드 2를 돌려준다. 예외가 아니라 <b>사유</b>로 나와야 엔진의
    /// 부분 실패 모델에 실린다(§5).
    /// </summary>
    [Fact]
    public void AMissingPathComesBackAsAReasonNotAnException()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"folderdeck-no-such-{Guid.NewGuid():N}.txt");

        var reason = _bin.Send(missing);

        Assert.Equal("원본이 없다", reason);
    }

    /// <summary>한글·공백·긴 경로가 섞여도 마샬링이 깨지지 않는다(유니코드 W 진입점).</summary>
    [Fact]
    public void AUnicodePathIsMarshalledIntact()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"폴더덱 테스트 파일 {Guid.NewGuid():N}.txt");

        var reason = _bin.Send(missing);

        // 경로가 깨졌다면 "경로가 잘못됐다"(0x7C)나 다른 코드가 나온다.
        Assert.Equal("원본이 없다", reason);
    }

    [Fact]
    public void AnEmptyPathIsARejectedArgumentNotASilentNoOp()
    {
        Assert.Throws<ArgumentException>(() => _bin.Send("   "));
        Assert.Throws<ArgumentNullException>(() => _bin.Send(null!));
    }
}
