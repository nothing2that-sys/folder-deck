using FolderDeck.Core.Models;

namespace FolderDeck.Core.Storage;

/// <summary>
/// 워크스페이스 목록 열거 결과 (§3.1 — 인덱스 파일이 아니라 <c>workspaces\*.json</c> 열거).
/// 손상된 파일 하나가 목록 전체를 못 보게 만들면 안 되므로, 읽힌 것과 실패한 것을 같이 돌려준다.
/// 실패는 런처가 사용자에게 보여줘야 한다(조용히 버리지 않는다, §5).
/// </summary>
public sealed record WorkspaceListing(
    IReadOnlyList<Workspace> Workspaces,
    IReadOnlyList<StorageFailure> Failures);

/// <summary>
/// <c>%APPDATA%\FolderDeck\</c> 의 JSON 저장소 (§5 / §7).
/// </summary>
/// <remarks>
/// 반환 규약: 값을 돌려주는 로드는 <see cref="StorageResult{T}"/>,
/// 값이 없는 저장/삭제는 <c>StorageFailure?</c>(null = 성공). 어느 쪽도 실패를 삼키지 않는다.
/// </remarks>
public interface IWorkspaceStore
{
    FolderDeckPaths Paths { get; }

    /// <summary>디바운스 저장의 백그라운드 실패. UI 스레드가 아닌 곳에서 올라온다.</summary>
    event EventHandler<StorageFailure>? SaveFailed;

    /// <summary><c>workspaces\*.json</c> 을 열거한다. 폴더가 없으면 빈 목록.</summary>
    WorkspaceListing ListWorkspaces();

    StorageResult<Workspace> LoadWorkspace(Guid id);

    StorageResult<Workspace> LoadWorkspaceFile(string filePath);

    /// <summary>지금 즉시 원자적으로 쓴다.</summary>
    StorageFailure? SaveWorkspace(Workspace workspace);

    /// <summary>
    /// 디바운스 저장 예약 (§5). 같은 워크스페이스의 앞선 예약을 대체한다.
    /// 예약 시점의 상태를 복사해 두므로 이후 모델 변경은 이 저장에 반영되지 않는다.
    /// </summary>
    void SaveWorkspaceDebounced(Workspace workspace);

    /// <summary><b>워크스페이스 JSON 만</b> 지운다. 실제 폴더는 건드리지 않는다 (§3.1).</summary>
    StorageFailure? DeleteWorkspace(Guid id);

    /// <summary>
    /// 설정을 읽는다. 파일이 없으면 <see cref="StorageFailureKind.NotFound"/> —
    /// 첫 실행이면 호출부가 기본값을 쓰면 되고, 손상(<see cref="StorageFailureKind.CorruptData"/>)과
    /// 구분된다.
    /// </summary>
    StorageResult<AppSettings> LoadSettings();

    StorageFailure? SaveSettings(AppSettings settings);

    void SaveSettingsDebounced(AppSettings settings);

    /// <summary>밀린 디바운스 저장을 지금 모두 수행한다. 종료 직전에 호출.</summary>
    void Flush();
}
