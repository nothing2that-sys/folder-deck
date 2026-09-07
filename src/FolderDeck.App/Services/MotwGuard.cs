using System.IO;

namespace FolderDeck.App.Services;

/// <summary>
/// MOTW(<c>Zone.Identifier</c> 대체 데이터 스트림) 판정 수단(Phase 17 · D98). ① 단
/// (미리보기 핸들러 선택)이 이것을 부르기 전에 격리 없는 서드파티 파서(핸들러)에게 남이
/// 만든 파일을 넘기지 않도록 막는다(D85-d 실측). <b>배선은 다음 회차(B)의 몫이다</b> —
/// 이 회차에서는 부르는 곳이 아직 없다.
/// </summary>
/// <remarks>
/// <b>판정에 실패하면 「있음」으로 친다</b>(D98 §3 결정 2) — 규칙 전체가 보수적 선택
/// 위에 섰으므로 실패 경로만 뚫리면 앞뒤가 안 맞는다. 그래서 이름이
/// <see cref="ShouldSkipPreviewHandler"/> 다(<c>HasMarkOfTheWeb</c> 이 아니다) — 실패 시
/// 반환이 「MOTW 가 있다」와 같은 <see langword="true"/> 라 이름과 동작이 맞는 쪽을 골랐다.
/// <para>
/// <see cref="FpuGuard"/> 로 감싸지 않는다(D98 §3 결정 4) — <see
/// cref="ShellThumbnailService"/> 와 달리 셸을 부르지 않고 <see cref="System.IO"/> 만
/// 쓴다. 감싸면 「셸 호출을 감싼다」는 관례가 흐려진다.
/// </para>
/// <para>
/// <c>try</c>/<c>catch</c> 를 두지 않는다(D98-b §3 결정 1) — <see cref="File.Exists"/> 는
/// 잘못된 문자·긴 경로·권한 없음 어느 경우에도 예외를 던지지 않고 <see
/// langword="false"/> 를 돌려준다. 이것은 API 계약을 근거로 한 판단이지 재서 확인한
/// 것이 아니다.
/// </para>
/// </remarks>
internal static class MotwGuard
{
    /// <summary>
    /// <paramref name="filePath"/> 에 <c>Zone.Identifier</c> 대체 데이터 스트림이 있으면
    /// (또는 판정 자체가 안 되면) <see langword="true"/> — 있고 없고만 본다. 스트림
    /// 내용도 <c>ZoneId</c> 값도 보지 않는다. 캐시를 두지 않는다.
    /// </summary>
    internal static bool ShouldSkipPreviewHandler(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return true;
        }

        if (!File.Exists(filePath))
        {
            // 존재하지 않는 경로도 폴더 경로도 File.Exists 는 예외 없이 false 를
            // 돌려준다 — 여기서 갈라내지 않으면 결정 2(D98 §3 결정 2 — 없는 경로 ·
            // 폴더 경로는 「있음」으로 친다)가 안 지켜진다.
            return true;
        }

        return File.Exists(filePath + ":Zone.Identifier");
    }
}
