using System.Collections.Generic;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderDeck.App.Services;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.ViewModels;

/// <summary>파일 목록의 한 줄. 열거 결과를 표시용으로 감싼다.</summary>
/// <remarks>
/// 표시 문자열은 프로퍼티에서 계산한다. 목록 가상화 덕분에 화면에 보이는 줄만
/// 실제로 계산되므로, 로그 수천 개짜리 폴더에서도 앞당겨 포맷하지 않는다.
/// </remarks>
public sealed partial class FileItemViewModel(
    FolderItem item,
    string relativeFolder = "",
    IReadOnlyDictionary<string, string>? extensionGlyphs = null,
    ShellIconService? shellIconService = null) : ObservableObject
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    private static readonly IReadOnlyDictionary<string, string> EmptyGlyphs =
        new Dictionary<string, string>();

    private readonly IReadOnlyDictionary<string, string> _extensionGlyphs = extensionGlyphs ?? EmptyGlyphs;

    private readonly ShellIconService? _shellIconService = shellIconService;

    private bool _shellIconRequested;

    private CancellationTokenSource? _shellIconCancellation;

    public FolderItem Item { get; } = item;

    /// <summary>
    /// 재귀 검색 결과의 위치 — 검색 기준 폴더를 뺀 상대 폴더. 평소 목록에서는 빈 문자열이다.
    /// </summary>
    public string RelativeFolder { get; } = relativeFolder;

    public bool HasRelativeFolder => RelativeFolder.Length > 0;

    /// <summary>list 뷰에서 이름 앞에 흐리게 붙이는 접두사.</summary>
    public string RelativeFolderPrefix =>
        HasRelativeFolder ? RelativeFolder + System.IO.Path.DirectorySeparatorChar : string.Empty;

    public string Name => Item.Name;

    public string FullPath => Item.FullPath;

    public bool IsDirectory => Item.IsDirectory;

    /// <summary>취득한 셸 아이콘(② 단). 없으면(못 얻었거나 아직 안 왔으면) <c>null</c> 이다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellIcon))]
    private BitmapSource? icon;

    /// <summary>
    /// 화면이 바인딩하는 셸 아이콘. getter 가 처음 불릴 때 취득을 한 번만 건다 — 목록
    /// 가상화 덕분에 보이는 줄에서만 불리므로 여기가 취득이 걸리는 자리다(Phase 11 · D68-c).
    /// 확장자 커스텀이 걸린 항목(<see cref="TryGetCustomGlyph"/>)은 ① 단이 이겨 셸을 부르지
    /// 않는다 — <see cref="Glyph"/> 와 같은 헬퍼를 쓴다.
    /// </summary>
    public BitmapSource? ShellIcon
    {
        get
        {
            EnsureShellIconRequested();
            return Icon;
        }
    }

    private void EnsureShellIconRequested()
    {
        if (_shellIconRequested || _shellIconService is null)
        {
            return;
        }

        _shellIconRequested = true;
        var service = _shellIconService;

        if (TryGetCustomGlyph(out _))
        {
            return;
        }

        if (service.TryGetCached(FullPath, IsDirectory, out var cached))
        {
            Icon = cached;
            return;
        }

        FetchShellIconAsync(service);
    }

    private async void FetchShellIconAsync(ShellIconService service)
    {
        var cancellation = new CancellationTokenSource();
        _shellIconCancellation = cancellation;

        try
        {
            var icon = await service.GetIconAsync(FullPath, IsDirectory, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                Icon = icon;
            }
        }
        catch
        {
            // 예외를 밖으로 내보내지 않는다 — 실패·취소하면 ③ 단(글리프)으로 남는다.
        }
        finally
        {
            if (ReferenceEquals(_shellIconCancellation, cancellation))
            {
                _shellIconCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// 화면 밖으로 스크롤돼 컨테이너가 재활용·폐기될 때 뷰가 부른다(2026-09-18 프로브 §1 —
    /// head-of-line blocking). 대기 중인 취득을 <see cref="ShellIconService"/> 의 게이트
    /// 대기열에서 빼고, 요청 플래그를 되돌려 나중에 다시 보이면 새로 걸 수 있게 한다 —
    /// 그래서 취소된 항목이 영원히 글리프로 굳지 않는다. 이미 끝났거나(캐시 적중·완료)
    /// 아직 한 번도 안 걸었으면 아무 일도 안 한다.
    /// </summary>
    internal void CancelPendingShellIconFetch()
    {
        if (_shellIconCancellation is null)
        {
            return;
        }

        _shellIconCancellation.Cancel();
        _shellIconRequested = false;
    }

    /// <summary>
    /// <see cref="Glyph"/> 와 <see cref="EnsureShellIconRequested"/> 가 함께 쓰는 확장자 매핑
    /// 판정. 걸려 있으면 <paramref name="glyph"/> 에 그 글자를 담고 <c>true</c> 를 낸다 —
    /// 걸려 있으면 셸을 부르지 않는다(① 단이 이긴다). 폴더는 매핑을 보지 않는다(늘 <c>false</c>).
    /// </summary>
    private bool TryGetCustomGlyph(out string glyph)
    {
        glyph = string.Empty;

        if (Item.IsDirectory)
        {
            return false;
        }

        var extension = AppSettings.NormalizeExtension(System.IO.Path.GetExtension(Item.Name));
        if (extension.Length > 0 && _extensionGlyphs.TryGetValue(extension, out var mapped) && mapped.Length > 0)
        {
            glyph = mapped;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 폴더는 늘 <c>📁</c> 다 — 확장자 매핑은 파일에만 걸린다(D59 결정 7). 파일은 사용자
    /// 매핑(<see cref="_extensionGlyphs"/>)에 확장자가 있으면 그 글자, 없으면 <c>📄</c> 다.
    /// 이 프로퍼티는 여전히 <b>유니코드 글리프만</b> 준다 — 셸 아이콘(② 단)은 별도 프로퍼티
    /// (<see cref="ShellIcon"/>)가 맡는다(Phase 11 · D68-c).
    /// </summary>
    /// <remarks>
    /// 두 갈래 어휘(<c>doc/FolderDeck_familylook_spec.md</c> §6 · 폴더 <c>📁</c>(U+1F4C1) ·
    /// 파일 <c>📄</c>(U+1F4C4))는 <b>매핑이 비어 있을 때의 결과와 같다</b>(D59 결정 6) —
    /// 확장자별 매핑은 그 어휘표 밖의 사용자 확장이다.
    /// <para>
    /// Phase 8-B ③ 에서 폴더를 <c>▣</c>(U+25A3) 로 바꿨다가 <b>8-C ① 에서 되돌렸다</b>.
    /// 바꾼 이유였던 "둘이 잘 안 갈린다"는 여전히 사실이다 — WPF 는 컬러 폰트를 그리지 않아
    /// 두 이모지가 같은 굵기의 가는 윤곽선으로 떨어진다. 다만 <b>그것을 형태가 아니라 색으로
    /// 푼다</b>: 어휘표를 지키면서 가르는 쪽이 낫다. 색은 뷰가 준다 —
    /// <c>Views/FolderPanelView.xaml</c> 의 <c>ItemGlyph</c> 스타일이
    /// <c>Brush.FolderGlyph</c>(황토)와 <c>Brush.FileGlyph</c>(무채색)를 갈라 붙인다. 이 색
    /// 트리거는 <see cref="IsDirectory"/> 만 보므로 확장자 매핑이 늘어도 손대지 않는다
    /// (D59 결정 7) — 파일은 확장자가 무엇이든 계속 무채색이다.
    /// </para>
    /// <para>
    /// <b>색만으로 나르는 것이 아니다.</b> "색만으로 구분하지 않는다"는 규칙(설계 §3.1 의
    /// <c>열려 있음</c> 배지 · §3.2 의 상시/순환 테두리)은 <b>글자가 없는 상태 표시</b>에
    /// 대한 것이다. 여기는 글리프 옆에 이름이 늘 붙어 있고, 상세 뷰에서는 크기 칸이 폴더만
    /// 비어 있어(<see cref="SizeText"/>) 색을 못 봐도 종류를 읽을 수 있다.
    /// </para>
    /// </remarks>
    public string Glyph
    {
        get
        {
            if (Item.IsDirectory)
            {
                return "📁";
            }

            return TryGetCustomGlyph(out var glyph) ? glyph : "📄";
        }
    }

    /// <summary>
    /// 타일 간 비교(기능 후보 ①, 2026-09-18 설계)의 판정. 비교가 안 걸려 있으면
    /// <see cref="CompareStatus.Unset"/>(기본값) — 배지를 그리는 쪽이 이 값으로 표시 여부를
    /// 정한다. <see cref="MainViewModel"/> 이 비교를 걸거나 풀 때만 바뀐다 — 이 항목 자체는
    /// 자기가 비교 중인지 모른다.
    /// </summary>
    [ObservableProperty]
    private CompareStatus compareStatus = CompareStatus.Unset;

    /// <summary>폴더는 비워 둔다 — 재귀 계산하지 않는다.</summary>
    public string SizeText => Item.IsDirectory ? string.Empty : Humanize(Item.Size);

    public string ModifiedText => Item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string Humanize(long? bytes)
    {
        if (bytes is null)
        {
            return string.Empty;
        }

        double size = bytes.Value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes.Value:N0} B" : $"{size:0.#} {Units[unit]}";
    }
}
