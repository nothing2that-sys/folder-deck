using System.IO;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Services;

/// <summary>
/// 마지막 안전망 — 어디서도 못 잡은 예외를 파일로 남긴다(검토 회차 발견: 이 앱에는 전역
/// 미처리 예외 핸들러도 파일 로깅도 전혀 없었다). 로그를 남긴다고 크래시를 막지는
/// 않는다 — 상태가 어디까지 깨졌는지 모르는 채로 계속 돌리는 쪽이 더 위험할 수 있어서다.
/// 목적은 오직 하나, 사용자 PC 에서 실제로 난 크래시를 사후에 알아낼 단서를 남기는 것.
/// 자동 갱신·코드 서명이 없는 배포(§12.5)와 맞물려, 이 로그가 없으면 사용자가 재현
/// 상황을 직접 설명해 주지 않는 한 원인을 알 방법이 아예 없다.
/// </summary>
internal static class CrashLogger
{
    /// <summary>
    /// 예외를 <c>%APPDATA%\FolderDeck\crashes\</c> 아래 타임스탬프 파일로 남긴다.
    /// 로깅 자체가 실패해도(디스크 꽉 참 등) 조용히 넘어간다 — 크래시 처리 중에 또
    /// 다른 크래시를 내지 않는다.
    /// </summary>
    internal static void TryWrite(string source, Exception exception)
    {
        try
        {
            var dir = Path.Combine(FolderDeckPaths.Default.Root, "crashes");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyy-MM-dd_HHmmss_fff}.log");
            var text =
                $"시각: {DateTime.Now:O}\n" +
                $"자리: {source}\n" +
                $"판: {typeof(CrashLogger).Assembly.GetName().Version}\n\n" +
                exception;

            File.WriteAllText(path, text);
        }
        catch
        {
            // 로깅 실패는 삼킨다 — 크래시 처리 중 두 번째 크래시를 만들지 않는다.
        }
    }
}
