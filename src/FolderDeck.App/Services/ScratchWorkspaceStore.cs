using System;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Services;

/// <summary>
/// 1회용 창(<c>--scratch</c>)의 저장소 (2026-10-14 S4). 그 작업 관리 하나에 대해서만 저장을 막고,
/// 나머지(설정·다른 작업 관리 읽기·<see cref="Flush"/>)는 안쪽 저장소로 넘긴다.
/// </summary>
/// <remarks>
/// <c>MainViewModel</c> 에 「저장 안 함」 플래그를 흩뿌리지 않고 이 래퍼 하나로 끝낸다 — 저장을 부르는
/// 자리가 뷰모델 안에만 여럿이라(<c>Save()</c> · <c>SaveWorkspaceDebounced</c> 직접 호출) 플래그 방식은
/// 하나를 빠뜨린다. 여기서 막으면 새 저장 자리가 늘어도 그대로 막힌다.
/// </remarks>
public sealed class ScratchWorkspaceStore(IWorkspaceStore inner, Guid scratchWorkspaceId) : IWorkspaceStore
{
    public Guid ScratchWorkspaceId { get; } = scratchWorkspaceId;

    public FolderDeckPaths Paths => inner.Paths;

    public event EventHandler<StorageFailure>? SaveFailed
    {
        add => inner.SaveFailed += value;
        remove => inner.SaveFailed -= value;
    }

    public WorkspaceListing ListWorkspaces() => inner.ListWorkspaces();

    public StorageResult<Workspace> LoadWorkspace(Guid id) => inner.LoadWorkspace(id);

    public StorageResult<Workspace> LoadWorkspaceFile(string filePath) => inner.LoadWorkspaceFile(filePath);

    public StorageFailure? SaveWorkspace(Workspace workspace) =>
        workspace.Id == ScratchWorkspaceId ? null : inner.SaveWorkspace(workspace);

    public void SaveWorkspaceDebounced(Workspace workspace)
    {
        if (workspace.Id != ScratchWorkspaceId)
        {
            inner.SaveWorkspaceDebounced(workspace);
        }
    }

    public StorageFailure? DeleteWorkspace(Guid id) =>
        id == ScratchWorkspaceId ? null : inner.DeleteWorkspace(id);

    public StorageResult<AppSettings> LoadSettings() => inner.LoadSettings();

    public StorageFailure? SaveSettings(AppSettings settings) => inner.SaveSettings(settings);

    public void SaveSettingsDebounced(AppSettings settings) => inner.SaveSettingsDebounced(settings);

    public void Flush() => inner.Flush();
}
