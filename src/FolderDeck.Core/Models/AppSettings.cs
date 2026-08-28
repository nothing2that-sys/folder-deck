namespace FolderDeck.Core.Models;

/// <summary>전역 설정 (§7.2). <c>%APPDATA%\FolderDeck\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// 1 → 2: <c>tileGap</c> 이 늘었다. 2 → 3: <c>extensionGlyphs</c> 가 늘었다(D59).
    /// <b>워크스페이스 스키마는 이것과 무관하다</b> — 간격도 아이콘 매핑도 창마다가 아니라
    /// 앱 전체의 값이다.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>저장소 소유. 저장 시 찍힌다 (<see cref="Workspace.SchemaVersion"/> 과 같은 규약).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>"마지막 작업을 자동으로 열기" — 런처 건너뛰기 (§3.1).</summary>
    public bool SkipLauncher { get; set; }

    public Guid? LastWorkspaceId { get; set; }

    /// <summary>런처 표시 순서(옵션). 여기 없는 워크스페이스는 뒤에 붙인다.</summary>
    public List<Guid>? WorkspaceOrder { get; set; }

    // ── 타일 사이 여백 (§3.2 · D56) ──────────────────────────────────────────

    /// <summary>
    /// 고를 수 있는 <b>다섯</b> — 가족룩 §5 램프다. <b>임의의 숫자는 받지 않는다.</b>
    /// </summary>
    public static readonly IReadOnlyList<int> TileGapChoices = [4, 6, 8, 12, 16];

    /// <summary>
    /// 슬롯이 없는 옛 파일과 램프 밖 값이 읽히는 값 — <b>지금 화면 그대로다.</b>
    /// </summary>
    /// <remarks>
    /// 기본값을 다르게 두면 <b>옛 설정의 화면이 조용히 바뀐다</b> — v6 <c>foldersFirst</c> 가
    /// 밟은 자리다. 그래서 이 값은 8 이고, 8 은 이 회차 전의 간격(칸 여백 4 + 4)과 같다.
    /// </remarks>
    public const int DefaultTileGap = 8;

    private int _tileGap = DefaultTileGap;

    /// <summary>
    /// <b>눈에 보이는 타일 사이 간격</b>(칸 여백이 아니다). 램프 밖 값은
    /// <see cref="DefaultTileGap"/> 으로 떨어진다.
    /// </summary>
    /// <remarks>
    /// <b>램프를 지키는 유일한 자리다.</b> 슬롯이 없는 파일은 필드 초기값이 그대로 남고
    /// (<c>System.Text.Json</c> 은 없는 속성의 setter 를 부르지 않는다), 슬롯이 있으면 이
    /// setter 를 지나므로 <b>두 경우가 모두 여기서 닫힌다</b> — 직렬화기의 행동에만 기대지
    /// 않는다는 뜻이다.
    /// </remarks>
    public int TileGap
    {
        get => _tileGap;
        set => _tileGap = TileGapChoices.Contains(value) ? value : DefaultTileGap;
    }

    // ── 확장자별 글리프 매핑 (§1.2 · D59) ────────────────────────────────────

    private Dictionary<string, string> _extensionGlyphs = [];

    /// <summary>
    /// 확장자(<see cref="NormalizeExtension"/> 을 거친 키) → 글리프 한 글자. <b>비어 있으면
    /// 지금(v1)과 같다</b> — 매핑에 없는 확장자는 그대로 <c>📄</c> 다(D59 결정 6). 기본
    /// 매핑은 깔지 않는다.
    /// </summary>
    /// <remarks>
    /// 슬롯이 없는 옛 파일은 필드 초기값(빈 사전)이 그대로 남는다(<c>System.Text.Json</c> 은
    /// 없는 속성의 setter 를 부르지 않는다). 슬롯이 있어도 값이 명시적으로 <c>null</c> 이면
    /// setter 가 <c>null</c> 로 불리므로 여기서 다시 빈 사전으로 되돌린다 — <see cref="TileGap"/>
    /// 이 램프 밖 값을 되돌리는 것과 같은 자리다. 두 경우 모두 여기서 닫히므로
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> 를 쓰는 코드 어디도
    /// <c>null</c> 을 볼 일이 없다.
    /// </remarks>
    public Dictionary<string, string> ExtensionGlyphs
    {
        get => _extensionGlyphs;
        set => _extensionGlyphs = value ?? [];
    }

    /// <summary>
    /// 확장자 키 정규화 — <b>넣을 때와 찾을 때가 반드시 이 함수 하나만 거치게 한다</b>(D59
    /// 결정 5). 앞뒤 공백을 자르고 소문자로 낮추고 앞점을 강제한다. <c>PDF</c> · <c>.PDF</c> ·
    /// <c>.pdf</c> 가 전부 <c>.pdf</c> 하나로 떨어진다.
    /// </summary>
    /// <remarks>
    /// 자르고 나서 빈 문자열이면 그대로 빈 문자열을 돌려준다 — 호출자가 "매핑 못 함"으로
    /// 걸러낸다(빈 문자열은 유효한 확장자 키가 아니다). 점만 있는 값(<c>"."</c>)은 그대로
    /// 유효한 키로 취급한다 — <c>Path.GetExtension</c> 이 이름이 점으로 끝나는 파일에 대해
    /// 실제로 이 값을 돌려주기 때문이다(<c>FolderEnumerator.cs</c>).
    /// </remarks>
    public static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var lower = trimmed.ToLowerInvariant();
        return lower[0] == '.' ? lower : "." + lower;
    }
}
