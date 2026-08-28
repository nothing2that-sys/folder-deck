using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>확장자→글리프 매핑 행 하나 — 설정 창 「아이콘」 탭의 한 줄(D59 결정 3).</summary>
public sealed partial class IconMappingRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string extension = string.Empty;

    [ObservableProperty]
    private string glyph = string.Empty;
}

/// <summary>
/// 설정 창 「아이콘」 탭의 DataContext (D59). 행을 더하고 지우는 목록 조각은
/// <c>LauncherViewModel.NewFolders</c> 를, 행 안 텍스트 편집은 <c>WorkspaceCardViewModel.
/// IsEditing</c> 을 본떴다 — 「빈 행을 만들어 그 자리에서 타이핑」만 이 클래스가 새로 짠다.
/// </summary>
public sealed partial class IconSettingsViewModel : ObservableObject
{
    public IconSettingsViewModel(IReadOnlyDictionary<string, string> current)
    {
        foreach (var pair in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Rows.Add(new IconMappingRowViewModel { Extension = pair.Key, Glyph = pair.Value });
        }
    }

    public ObservableCollection<IconMappingRowViewModel> Rows { get; } = [];

    /// <summary>＋ — 빈 행을 더한다. 지금 앱에 없던 패턴이라 새로 짠다(D59 결정 3).</summary>
    [RelayCommand]
    public void AddRow() => Rows.Add(new IconMappingRowViewModel());

    /// <summary>✕ — 이 행을 지운다.</summary>
    [RelayCommand]
    public void RemoveRow(IconMappingRowViewModel? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>
    /// 저장할 사전으로 굳힌다 — 키는 <see cref="AppSettings.NormalizeExtension"/> 하나만
    /// 거친다(D59 결정 5). 확장자나 글리프 중 하나라도 비어 있는 행은(자르고 난 뒤 기준)
    /// 빈 매핑에서 제외한다 — 반쯤 채운 행이 조용히 <c>📄</c> 판정에 끼어들지 않는다.
    /// 같은 키로 정규화되는 행이 여럿이면 <b>목록의 나중 행이 이긴다</b>(뒤에서 덮어쓰므로).
    /// </summary>
    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>();

        foreach (var row in Rows)
        {
            var key = AppSettings.NormalizeExtension(row.Extension);
            var glyph = row.Glyph.Trim();
            if (key.Length == 0 || glyph.Length == 0)
            {
                continue;
            }

            result[key] = glyph;
        }

        return result;
    }
}
