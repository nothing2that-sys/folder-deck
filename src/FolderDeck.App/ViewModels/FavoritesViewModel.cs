using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.ViewModels;

/// <summary>왼쪽 「즐겨찾기」 묶음의 한 줄 (2026-10-14 S3).</summary>
public sealed partial class FavoriteRowViewModel(FavoriteEntry entry) : ObservableObject
{
    public FavoriteEntry Entry { get; } = entry;

    public Guid Id => Entry.Id;

    public string Path => Entry.Path;

    public string DisplayName => string.IsNullOrWhiteSpace(Entry.DisplayName)
        ? System.IO.Path.GetFileName(Entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Entry.Path
        : Entry.DisplayName!;

    /// <summary>
    /// 지금 열 수 있는 폴더인가. <b>처음엔 참</b>으로 두고 <see cref="FavoritesViewModel.ProbeAsync"/>
    /// 가 UI 스레드 밖에서 확인해 내린다 — 닿지 않는 네트워크 경로의 <c>Directory.Exists</c> 는 수 초를
    /// 막으므로 화면을 그리는 길에서 부르지 않는다.
    /// </summary>
    [ObservableProperty]
    private bool isAvailable = true;
}

/// <summary>
/// 모든 작업 관리가 함께 쓰는 즐겨찾기 묶음 (2026-10-14 S3). 등록 폴더(<see cref="FolderRowViewModel"/>)와
/// 분리된 「바로 이동」 목록이다 — 클릭이 무엇을 하는지는 <see cref="MainViewModel.OpenFavoriteAsync"/> 가 정한다.
/// </summary>
/// <remarks>
/// <b>쓰기는 언제나 <see cref="FavoritesStore"/> 의 디스크 다시 읽기 위에서</b> 한다. 이 클래스의
/// <see cref="Rows"/> 는 화면용 사본이고, 쓰기 뒤에는 저장소가 돌려준 목록으로 통째로 갈아 끼운다 —
/// 그래야 그 사이 다른 창이 한 변경도 같이 보인다. 다른 창의 변경을 알아채는 시점(파일 감시)은
/// 뷰의 몫이다(<c>MainWindow</c>) — 여기는 <see cref="Reload"/> 만 연다.
/// </remarks>
public sealed partial class FavoritesViewModel : ObservableObject
{
    private readonly FavoritesStore _store;
    private readonly Action<string> _inform;
    private readonly Action<string> _warn;

    public FavoritesViewModel(FavoritesStore store, bool collapsed, Action<string> inform, Action<string> warn)
    {
        _store = store;
        _inform = inform;
        _warn = warn;
        isCollapsed = collapsed;
    }

    public ObservableCollection<FavoriteRowViewModel> Rows { get; } = [];

    /// <summary>감시할 파일 — 뷰가 다른 창의 변경을 알아채려고 본다.</summary>
    public string FilePath => _store.FilePath;

    /// <summary>묶음 접힘 — 전역 설정(<see cref="AppSettings.FavoritesCollapsed"/>)에 산다.</summary>
    [ObservableProperty]
    private bool isCollapsed;

    /// <summary>접힘이 바뀌었을 때 설정 저장을 부탁할 자리. 뷰모델은 설정 저장소를 모른다.</summary>
    public Action<bool>? CollapsedChanged { get; set; }

    partial void OnIsCollapsedChanged(bool value) => CollapsedChanged?.Invoke(value);

    [RelayCommand]
    public void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>파일을 다시 읽어 화면을 맞춘다. 손상·모르는 판이면 목록을 비우지 않고 알리기만 한다.</summary>
    public void Reload()
    {
        var loaded = _store.Load();
        if (loaded.Value is not { } items)
        {
            _warn($"즐겨찾기를 읽지 못했다 — {loaded.Failure}");
            return;
        }

        Replace(items);
    }

    /// <summary>경로 하나를 더한다. 이미 있으면 알리기만 한다.</summary>
    public void AddPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = _store.Add(path);
        if (!Apply(result))
        {
            return;
        }

        if (result.Value!.Changed)
        {
            _inform($"즐겨찾기에 추가했다: {path}");
        }
        else
        {
            _inform($"이미 즐겨찾기에 있다: {path}");
        }
    }

    [RelayCommand]
    public void Remove(FavoriteRowViewModel? row)
    {
        if (row is not null)
        {
            Apply(_store.Remove(row.Id));
        }
    }

    [RelayCommand]
    public void MoveUp(FavoriteRowViewModel? row)
    {
        if (row is not null)
        {
            Apply(_store.Move(row.Id, -1));
        }
    }

    [RelayCommand]
    public void MoveDown(FavoriteRowViewModel? row)
    {
        if (row is not null)
        {
            Apply(_store.Move(row.Id, +1));
        }
    }

    /// <summary>각 줄이 지금 열 수 있는지 UI 스레드 밖에서 확인한다.</summary>
    public async Task ProbeAsync()
    {
        foreach (var row in Rows.ToList())
        {
            var path = row.Path;
            row.IsAvailable = await Task.Run(() => System.IO.Directory.Exists(path)).ConfigureAwait(true);
        }
    }

    private bool Apply(StorageResult<FavoritesMutation> result)
    {
        if (result.Value is not { } mutation)
        {
            _warn($"즐겨찾기를 바꾸지 못했다 — {result.Failure}");
            return false;
        }

        Replace(mutation.Items);
        return true;
    }

    private void Replace(IReadOnlyList<FavoriteEntry> items)
    {
        // 열 수 있는지는 이전 줄의 판정을 이어받는다 — 다시 읽을 때마다 깜빡이지 않게.
        var previous = Rows.ToDictionary(r => r.Id, r => r.IsAvailable);

        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(new FavoriteRowViewModel(item)
            {
                IsAvailable = !previous.TryGetValue(item.Id, out var available) || available,
            });
        }
    }
}
